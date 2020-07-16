﻿using Plugin.FilePicker;
using Plugin.FilePicker.Abstractions;
using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Windows.Input;
using Xamarin.Forms;
using System.Threading.Tasks;
using VerdeNFC.Services;
using VerdeNFC.Util;
using Plugin.Toast;
using VerdeNFC.Models;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace VerdeNFC.ViewModels
{
    public class PauseDuration : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // minutes
            int v1 = GetMinutes((int)value);

            if (v1 < 60)
                return string.Format("{0} min", v1);
            if (v1 < 1440)
                return string.Format("{0}h {1}min", (int)(v1 / 60), v1 % 60);
            return string.Format("{0} days {1}h {2}min", (int)v1 / 1440, (int)((v1 % 1440) / 60), v1 % 60);
        }

        public static int GetMinutes(int v)
        {
            // first 2 hours - 1 minute steps 
            if (v < 120)
                return v;
            // 2-6 hours - 5 minute steps - (6-2)*12=48
            else if (v < 168)
                return 120 + 5 * (v - 120);
            // 6-24h -  15min steps (24-6)*4 = 72 steps
            else //if (v < 240)
                return 360 + 15 * (v - 168);
        }

        public static int GetSliderTicks(int v)
        {
            // first 2 hours - 1 minute steps 
            if (v < 120)
                return v;
            // 2-6 hours - 5 minute steps - (6-2)*12=48
            else if (v < 360)
                return (int)(v - 120) / 5 + 120;
            // 6-24h -  15min steps (24-6)*4 = 72 steps
            else //if (v < 240)
                return (v-360) / 15 + 168;

        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class MainTabViewModel : BaseViewModel
    {
        public static MainTabViewModel Current;

        public IDataBag DataBag => DependencyService.Get<IDataBag>();
        
        public delegate void NFCControlListening(bool Write);
        public event NFCControlListening NFCStartListening;
        public event NFCControlListening NFCStopListening;

        #region RoastProfilesPicker
        private ObservableCollection<RoastProfile> _roastProfiles = new ObservableCollection<RoastProfile>();
        public ObservableCollection<RoastProfile> RoastProfiles
        {
            get { return _roastProfiles; }
            set
            {
                _roastProfiles = value;
                OnPropertyChanged(nameof(RoastProfiles));
            }
        }

        private RoastProfile lastSelectedRoastProfile;
        private RoastProfile _roastProfileSel = new RoastProfile();
        public RoastProfile RoastProfileSel
        {
            get { return _roastProfileSel; }
            set
            {
                _roastProfileSel = value;
                OnPropertyChanged(nameof(RoastProfileSel));
            }
        }

        public void RoastProfileSelChanged()
        {
            // if changed manually in picker (this function is also called, if we programmatically change RoastProfileSel)m
            // we want to remove formerly selected special (id >= 100) entry, so it connot be choosen manually in the picker again
            if (RoastProfileSel.isManualChoiceAllowed ||                       // manually template choosen or
                (RoastProfileSel.Id != lastSelectedRoastProfile.Id))           // programmatically other special entry choosen
            {
                if ((lastSelectedRoastProfile?.Id > 100) || (lastSelectedRoastProfile?.Id == 0))
                {
                    RoastProfiles.Remove(lastSelectedRoastProfile);
                    OnPropertyChanged(nameof(RoastProfiles));
                }
            }

            if (!string.IsNullOrEmpty(RoastProfileSel.Data))
            {
                // if (manually) template choosen -> load template
                byte[] mem = new byte[80];
                int index = 16;
                string line = RoastProfileSel.Data;
                if (string.IsNullOrEmpty(line))
                    return;

                try
                {
                    int j = 0;
                    while ((j < line.Length) && (index < 80))
                    {
                        if ((line[j] == ' ') || (line[j] == '\t'))
                        {
                            j++;
                            continue;
                        }

                        mem[index++] = byte.Parse(line.Substring(j, 2), NumberStyles.HexNumber);
                        j += 2;
                    }
                }
                catch
                {
                    Console.WriteLine("Error: unknown line format: {0}", line);
                }

                nPause = PauseDuration.GetSliderTicks(256 * mem[48] + mem[49]);
                SetControlsVisibility(mem[41], false);

                DataBag.SetData(mem);
                MessagingCenter.Send(this, "DataChanged", DataBag.GetData());
            }

            lastSelectedRoastProfile = RoastProfileSel;
        }
        #endregion

        #region NFCReadButton
        bool _cbNFCRead;
        public bool cbNFCRead
        {
            get
            {
                return _cbNFCRead;
            }
            set
            {
                if (value == _cbNFCRead)
                    return;

                try
                {
                    if (value)
                        NFCStartListening?.Invoke(false);
                    else
                    {
                        NFCStopListening?.Invoke(false);
                        MessagingCenter.Send(this, "DataChanged", DataBag.GetData());
                    }

                    _cbNFCRead = value;
                }
                catch (Exception e)
                {
                    CrossToastPopUp.Current.ShowToastMessage("NFC not enabled/error");
                    _cbNFCRead = false;
                }
                OnPropertyChanged("cbNFCRead");
                OnPropertyChanged("cbNFCWriteEnabled");
            }
        }
        public bool cbNFCReadEnabled
        {
            get
            {
                return !_cbNFCWrite;
            }
        }
        #endregion
        
        #region NFCWriteButton
        public bool cbNFCWriteEnabled
        {
            get
            {
                return !_cbNFCRead;
            }
        }

        bool _cbNFCWrite;
        public bool cbNFCWrite
        {
            get
            {
                return _cbNFCWrite;
            }
            set
            {
                if (value == _cbNFCWrite)
                    return;

                try
                {
                    if (value)
                        NFCStartListening?.Invoke(true);
                    else
                    { 
                        NFCStopListening?.Invoke(true);
                        MessagingCenter.Send(this, "DataChanged", DataBag.GetData());
                    }

                    _cbNFCWrite = value;
                }
                catch (Exception e)
                {
                    CrossToastPopUp.Current.ShowToastMessage("NFC not enabled/error");
                    _cbNFCWrite = false;
                }
                OnPropertyChanged("cbNFCWrite");
                OnPropertyChanged("cbNFCReadEnabled");
            }
        }
        #endregion
        
        #region MultiUseCB
        bool _cbMultiUse;
        public bool cbMultiUse
        {
            get
            {
                return _cbMultiUse;
            }
            set
            {
                _cbMultiUse = value;
                OnPropertyChanged("cbMultiUse");
            }
        }
        private bool _cbMultiUseEnabled;
        public bool cbMultiUseEnabled
        {
            get
            {
                return _cbMultiUseEnabled;
            }
            set
            {
                _cbMultiUseEnabled = value;
                OnPropertyChanged("cbMultiUseEnabled");
            }
        }
        #endregion

        #region nPause
        private int _nPause;
        public int nPause
        { 
            get
            {
                return _nPause;
            }
            set
            {
                _nPause = value;
                byte[] mem = DataBag.GetData();
                mem[48] = Convert.ToByte(PauseDuration.GetMinutes(_nPause) / 256);
                mem[49] = Convert.ToByte(PauseDuration.GetMinutes(_nPause) % 256);
                DataBag.SetData(mem);
                OnPropertyChanged("nPause");
                MessagingCenter.Send(this, "DataChanged", DataBag.GetData());
            }
        }

        private bool _nPauseEnabled;
        public bool nPauseEnabled
        {
            get
            {
                return _nPauseEnabled;
            }
            set
            {
                _nPauseEnabled = value;
                OnPropertyChanged("nPauseEnabled");
            }
        }
        #endregion

        readonly string _downloadFolder;

        public MainTabViewModel()
        {
            Title = "VerdeNFC 1.0";

            OpenFilePickerSrc = new Command(async () => await OpenFilePickerSrcAsync());
            OpenFilePickerDest = new Command(async () => await OpenFilePickerDestAsync());
            cbMultiUse = true;
            Current = this;
            _downloadFolder = (string) Application.Current.Properties["FileSaveFolder"];
            _nPause = 0;
            cbMultiUse = true;

            // 0      - nothing choosen
            // 1...99 - data source is Data member
            // 100..  - external data source (file or scanned NFC tag)

            RoastProfiles.Add(new RoastProfile() { Id = 1, Name = "Brazil",                        Data = "AAB84B4B00 AAB8324B5A  AAB64B4B00 AAB6324B64 3C3C462D 32 02  1E  000501 D810 0005", isManualChoiceAllowed = true });
            RoastProfiles.Add(new RoastProfile() { Id = 2, Name = "80g Roast only",                Data = "AAAE4B5A05 AAAE415078  AAB4415A05 AAB4415050 37465A50 23 02  0F  000601 6113 0000", isManualChoiceAllowed = true });
            RoastProfiles.Add(new RoastProfile() { Id = 3, Name = "universal light roast",         Data = "AAB94B3205 AAB94B325A  AAB94B3205 AAB94B3232 3C463250 32 02  1E  000601 5D03 000A", isManualChoiceAllowed = true });
            RoastProfiles.Add(new RoastProfile() { Id = 4, Name = "universal medium roast",        Data = "AA645A3205 AA9632327D  AAB4463C05 AAC0323C78 376E3C2D 23 02  0F  000601 F501 000A", isManualChoiceAllowed = true });
            RoastProfiles.Add(new RoastProfile() { Id = 5, Name = "universal dark roast ",         Data = "AA645A3205 AA9632327D  AAB4463C05 AAC0323C96 376E3C2D 23 02  0F  000601 8312 000A", isManualChoiceAllowed = true });
            RoastProfiles.Add(new RoastProfile() { Id = 6, Name = "80g Slow Roast RGB (10h)",      Data = "AAB84B4B00 AAB832465A  AAB64B5A00 AAB6324664 3C465A2D 32 01  2D  000501 CCFE 0258", isManualChoiceAllowed = true });

            RoastProfiles.Add(new RoastProfile() { Id = 7, Name = "80g Roasters Grind/Brew",       Data = "AAB0644B05 AAB0414678  AABA5F5A05 AABA41463C 376E5A2D 23 05  1E  000601 AB08 0000", isManualChoiceAllowed = true });
            RoastProfiles.Add(new RoastProfile() { Id = 8, Name = "20g Roasters Grind/Brew",       Data = "AAB44B4B00 AAB432464B  AAAE4B5A00 AAAE32465A 3C6E5A14 14 05  2D  000501 4A01 0000", isManualChoiceAllowed = true });

            RoastProfiles.Add(new RoastProfile() { Id = 90, Name = "Brew only 80g",                Data = "AA41644B05 AA4B413278  AA565F5005 AA5D414696 37375A2D 23 06  0F  000601 2BF2 0000", isManualChoiceAllowed = true });
            RoastProfiles.Add(new RoastProfile() { Id = 91, Name = "Maintenance: Air Filter",      Data = "AA96644B05 AA96413278  AAAC5F5005 AAB6414696 376E5A2D 23 0F  0F  000601 8D7C 0000", isManualChoiceAllowed = true });
            RoastProfiles.Add(new RoastProfile() { Id = 92, Name = "Maintenance: Descale",         Data = "AAB44B4B05 AAB44B465F  AAB24B5005 AAB24B4687 37465A2D 23 13  2D  000601 7F56 0007", isManualChoiceAllowed = true });
            RoastProfiles.Add(new RoastProfile() { Id = 93, Name = "Maintenance: Grinder clean",   Data = "AAB44B4B00 AAB432464E  AAB44B5A99 AAB4324666 3C465A2D 32 12  1E  000501 8D5E 0005", isManualChoiceAllowed = true });

            RoastProfiles.Add(new RoastProfile() { Id = 0, Name = "(choose one)", Data = "", isManualChoiceAllowed = false });

            RoastProfileSel = RoastProfiles[RoastProfiles.Count - 1];
            lastSelectedRoastProfile = RoastProfiles[RoastProfiles.Count - 1];

        }

        public async Task OpenFilePickerSrcAsync()
        {
            try
            {
                FileData fileData = await CrossFilePicker.Current.PickFile();
                if (fileData == null)
                    return; // user canceled file picking

                MemoryStream ms = new MemoryStream(fileData.DataArray);
                StreamReader reader = new StreamReader(ms, System.Text.Encoding.ASCII);

                string line;
                byte[] mem = new byte[80];
                int index = 0;

                while (true)
                {
                    line = reader.ReadLine();
                    if (line == null)
                        break;

                    try
                    {
                        int j = 0;
                        while ((2 * j < line.Length) &&
                               (line[2 * j] != ' ') &&
                               (line[2 * j] != '\t') &&
                               (index < 80))
                            mem[index++] = byte.Parse(line.Substring(2 * j++, 2), NumberStyles.HexNumber);
                    }
                    catch
                    {
                        Console.WriteLine("Error: unknown line format: {0}", line);
                    }
                }

                nPause = PauseDuration.GetSliderTicks(256 * mem[48] + mem[49]);
                SetControlsVisibility(mem[41]);

                DataBag.SetData(mem);
                MessagingCenter.Send(this, "DataChanged", DataBag.GetData());

            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception choosing file: " + ex.ToString());
            };

            return;
        }

        public async Task OpenFilePickerDestAsync()
        {
            try
            {
                FileData fileData = await CrossFilePicker.Current.PickFile();
                if (fileData == null)
                    return; // user canceled file picking

                MemoryStream ms = new MemoryStream(fileData.DataArray);
                StreamReader reader = new StreamReader(ms, System.Text.Encoding.ASCII);

                string line;
                byte[] mem = new byte[80];
                int index = 0;

                int i = 0;
                while (true)
                {
                    line = reader.ReadLine();
                    if (line == null)
                        break;

                    try
                    {
                        int j = 0;
                        while ((2 * j < line.Length) &&
                               (line[2 * j] != ' ') &&
                               (line[2 * j] != '\t') &&
                               (index < 80))
                            mem[index++] = byte.Parse(line.Substring(2 * j++, 2), NumberStyles.HexNumber);
                    }
                    catch
                    {
                        Console.WriteLine("Error: unknown line format: {0}", line);
                    }
                }

                byte[] mem1stCard = MergeTagData(DataBag.GetData(), mem);


                StreamWriter ofile = new StreamWriter(Path.Combine(_downloadFolder, fileData.FileName + "-new.txt"))
                {
                    NewLine = "\n"
                };

                index = 0;
                for (i = 0; i < 20; i++)
                {
                    line = string.Format("{0:X2}{1:X2}{2:X2}{3:X2}", mem1stCard[index], mem1stCard[index + 1], mem1stCard[index + 2], mem1stCard[index + 3]);
                    index += 4;
                    ofile.WriteLine(line);
                }

                ofile.Close();

            }
            catch (Exception ex)
            {
                System.Console.WriteLine("Exception choosing file: " + ex.ToString());
            };

            return;
        }

        public void SetControlsVisibility(byte Type, bool externalData=true)
        {
            RoastProfile r;
            switch (Type)
            {
                case 1:
                    r = new RoastProfile() { Id = 101, Name = "(RGB from NFC or File)", Data = "", isManualChoiceAllowed = false };
                    nPauseEnabled = true;
                    cbMultiUseEnabled = true;
                    break;
                case 2:
                    r = new RoastProfile() { Id = 102, Name = "(Roast from NFC or File)", Data = "", isManualChoiceAllowed = false };
                    nPauseEnabled = false;
                    cbMultiUseEnabled = true;
                    break;
                case 4:
                    r = new RoastProfile() { Id = 104, Name = "(Grind from NFC or File)", Data = "", isManualChoiceAllowed = false };
                    nPauseEnabled = false;
                    cbMultiUseEnabled = true;
                    break;
                case 5:
                    r = new RoastProfile() { Id = 103, Name = "(Grind/Brew from NFC or File)", Data = "", isManualChoiceAllowed = false };
                    nPauseEnabled = false;
                    cbMultiUseEnabled = true;
                    break;
                case 6:
                    r = new RoastProfile() { Id = 105, Name = "(Brew from NFC or File)", Data = "", isManualChoiceAllowed = false };
                    nPauseEnabled = false;
                    cbMultiUseEnabled = true;
                    break;
                case 0x0f:
                    r = new RoastProfile() { Id = 91, Name = "Air Filter Reset", Data = "AA96644B05 AA96413278  AAAC5F5005 AAB6414696 376E5A2D 23 0F  0F  000601 8D7C 0000", isManualChoiceAllowed = true };
                    nPauseEnabled = false;
                    cbMultiUseEnabled = true;
                    break;
                case 0x12:
                    r = new RoastProfile() { Id = 93, Name = "Maintenance: Grinder clean", Data = "AAB44B4B00 AAB432464E  AAB44B5A99 AAB4324666 3C465A2D 32 12  1E  000501 8D5E 0005", isManualChoiceAllowed = true };
                    nPauseEnabled = false;
                    cbMultiUseEnabled = true;
                    break;
                case 0x13:
                    r = new RoastProfile() { Id = 92, Name = "Maintenance: Descale", Data = "AAB44B4B05 AAB44B465F  AAB24B5005 AAB24B4687 37465A2D 23 13  2D  000601 7F56 0007", isManualChoiceAllowed = true };
                    nPauseEnabled = false;
                    cbMultiUseEnabled = true;
                    break;

                // RoastProfiles.Add(new RoastProfile() { Id = 101, Name = "(RGB from NFC or File)", Data = "", isManualChoiceAllowed = true});
                // RoastProfiles.Add(new RoastProfile() { Id = 102, Name = "(Roast from NFC or File)", Data = "", isManualChoiceAllowed = true});
                // RoastProfiles.Add(new RoastProfile() { Id = 103, Name = "(Grind/Brew from NFC or File)", Data = "", isManualChoiceAllowed = false});
                // RoastProfiles.Add(new RoastProfile() { Id = 104, Name = "(Grind from NFC or File)", Data = "", isManualChoiceAllowed = false});
                // RoastProfiles.Add(new RoastProfile() { Id = 105, Name = "(Brew from NFC or File)", Data = "", isManualChoiceAllowed = false});
                // RoastProfiles.Add(new RoastProfile() { Id = 101, Name = "(RGB from NFC or File)", Data = "", isManualChoiceAllowed = true});
                // RoastProfiles.Add(new RoastProfile() { Id = 102, Name = "(Roast from NFC or File)", Data = "", isManualChoiceAllowed = true});
                // RoastProfiles.Add(new RoastProfile() { Id = 103, Name = "(Grind/Brew from NFC or File)", Data = "", isManualChoiceAllowed = false});
                // RoastProfiles.Add(new RoastProfile() { Id = 104, Name = "(Grind from NFC or File)", Data = "", isManualChoiceAllowed = false});
                // RoastProfiles.Add(new RoastProfile() { Id = 105, Name = "(Brew from NFC or File)", Data = "", isManualChoiceAllowed = false});

                default:
                    r = new RoastProfile() { Id = 92, Name = "(unknown)", Data = "", isManualChoiceAllowed = false };
                    nPauseEnabled = false;
                    cbMultiUseEnabled = true;
                    break;
            }
            
            if (externalData)
            {
                if (RoastProfiles.Where(p => p.Id == r.Id).FirstOrDefault() == null)
                    RoastProfiles.Add(r);

                // workaround for throwing OutOfRangeException in OnRoastProfileSelChanged on ios
                if ((lastSelectedRoastProfile?.Id > 100) || (lastSelectedRoastProfile?.Id == 0))
                    RoastProfiles.Remove(lastSelectedRoastProfile);

                RoastProfileSel = RoastProfiles.Where(p => p.Id == r.Id).First();
            }
        }

        public static byte [] MergeTagData(byte [] mem1stCard, byte [] mem)
        {
            Buffer.BlockCopy(mem, 0, mem1stCard, 0, 16);

            if ((Current != null) && (Current.cbMultiUse))
            {
                mem1stCard[46] = Convert.ToByte(VerdeChecksums.crc16_multiuse(mem1stCard) & 0xff);
                mem1stCard[47] = Convert.ToByte((VerdeChecksums.crc16_multiuse(mem1stCard) >> 8) & 0xff);
            }
            else
            {
                mem1stCard[46] = Convert.ToByte(VerdeChecksums.crc16_singleuse(mem1stCard) & 0xff);
                mem1stCard[47] = Convert.ToByte((VerdeChecksums.crc16_singleuse(mem1stCard) >> 8) & 0xff);
            }

            mem1stCard[60] = Convert.ToByte(Crc8.ComputeChecksum(mem1stCard.Skip(0).Take(12).Concat(mem1stCard.Skip(16).Take(44)).ToArray()) & 0xff);

            Current.DataBag.SetData(mem1stCard);
            MessagingCenter.Send(Current, "DataChanged", Current.DataBag.GetData());

            return mem1stCard;
        }

        public ICommand OpenFilePickerSrc { get; }
        public ICommand OpenFilePickerDest { get; }
    }
}