﻿using System;
using System.Linq;
using Android.App;
using Android.Content;
using Android.Nfc;
using Android.Nfc.Tech;
using System.IO;
using Android;
using Android.Content.PM;
using VerdeNFC.ViewModels;
using Xamarin.Essentials;
using System.Threading;

namespace VerdeNFC.Droid
{
    public class NfcIo
    {

        private static Func<Activity> _activityResolver;
        
        static NfcIo _current;
        public static NfcIo Current { get { if (_current == null) _current = new NfcIo(); return _current; } }

        public static Activity CurrentActivity => GetCurrentActivity();

        public static void SetCurrentActivityResolver(Func<Activity> activityResolver)
        {
            _activityResolver = activityResolver;
        }

        public static void OnNewIntent(Intent intent)
        {
            Current?.CheckForNfcMessage(intent);
        }

        private static Activity GetCurrentActivity()
        {
            if (_activityResolver == null)
                throw new InvalidOperationException("Resolver for the current activity is not set. Call CrossNfc.SetCurrentActivityResolver somewhere in your startup code.");

            return _activityResolver();
        }

        private readonly NfcAdapter _nfcAdapter;
        private bool WriteMode;
        private bool Enabled; 

        public delegate void TagDetectedDelegate(Tag tag);
        public event TagDetectedDelegate TagDetected;

        public NfcIo()
        {
            _nfcAdapter = NfcAdapter.GetDefaultAdapter(CurrentActivity);
            WriteMode = false;
            Enabled = false;
        }

        public bool IsAvailable()
        {
            var context = Application.Context;
            if (context.CheckCallingOrSelfPermission(Manifest.Permission.Nfc) != Permission.Granted)
                return false;

            return _nfcAdapter != null;
        }

        public bool IsEnabled()
        {
            return _nfcAdapter?.IsEnabled ?? false;
        }

        public void StartListening(bool Write)
        {
            if (!IsAvailable())
                throw new InvalidOperationException("NFC not available");

            if (!IsEnabled()) // todo: offer possibility to open dialog
                throw new InvalidOperationException("NFC is not enabled");
            
            WriteMode = Write;

            var ndefDetected = new IntentFilter(NfcAdapter.ActionNdefDiscovered);
            ndefDetected.AddDataType("*/*");
            var tagDetected = new IntentFilter(NfcAdapter.ActionTagDiscovered);
            tagDetected.AddDataType("*/*");
            var filters = new[] { tagDetected };
            var intent = new Intent(CurrentActivity, CurrentActivity.GetType()).AddFlags(ActivityFlags.SingleTop);
            var pendingIntent = PendingIntent.GetActivity(CurrentActivity, 0, intent, 0);
            _nfcAdapter.EnableForegroundDispatch(CurrentActivity, pendingIntent, filters, new[] { new[] { Java.Lang.Class.FromType(typeof(MifareUltralight)).Name } });
            Enabled = true;
            //_nfcAdapter.EnableReaderMode(activity, this, NfcReaderFlags.NfcA | NfcReaderFlags.NoPlatformSounds, null);
        }

        public void StopListening(bool Dummy)
        {
            Enabled = false;
            //_nfcAdapter?.DisableReaderMode(CrossNfc.CurrentActivity);
            //_nfcAdapter?.DisableForegroundDispatch(CurrentActivity); // can be called from OnResume only
        }

        internal void CheckForNfcMessage(Intent intent)
        {
            if (!Enabled)
                return; 

            if (intent.Action != NfcAdapter.ActionTechDiscovered)
                return;

            if (!(intent.GetParcelableExtra(NfcAdapter.ExtraTag) is Tag tag))
                return;

            try
            {
                var ev1 = MifareUltralight.Get(tag);

                //TagDetected?.Invoke(tag);

                ev1.Connect();
                byte[] FirstTag = MainTabViewModel.Current?.DataBag.GetData();
                byte[] mem = new byte[80];

                for (int i = 0; i < 20; i += 4)
                {
                    byte[] payload = ev1.ReadPages(i);
                    Buffer.BlockCopy(payload, 0, mem, 4 * i, 16);
                }

                if (WriteMode)
                {
                    byte[] dstData = MainTabViewModel.MergeTagData(FirstTag, mem);


                    // password auth
                    // var response = ev1.Transceive(new byte[]{
                    //            (byte) 0x1B, // PWD_AUTH
                    //            0,0,0,0 });

                    // Check if PACK is matching expected PACK
                    // This is a (not that) secure method to check if tag is genuine
                    //if ((response != null) && (response.Length >= 2))
                    //{
                    //}

                    for (int i = 4; i < 16; i++)
                        ev1.WritePage(i, dstData.Skip(4 * i).Take(4).ToArray());

                    MainTabViewModel.Current?.DataBag.SetData(dstData);
                    WriteMode = false;
                }
                else
                {
                    MainTabViewModel.Current?.SetControlsVisibility(mem[41]);
                    MainTabViewModel.Current?.DataBag.SetData(mem);
                }
                ev1.Close();
                MainTabViewModel.Current.cbNFCRead = false;
                MainTabViewModel.Current.cbNFCWrite = false;

                try
                {
                    // Use default vibration length
                    Vibration.Vibrate();
                }
                catch (FeatureNotSupportedException ex)
                {
                    // Feature not supported on device
                }
                catch (Exception ex)
                {
                    // Other error has occurred.
                }
            }
            catch (Exception e)
            {
                try
                {
                    Vibration.Vibrate();
                    Thread.Sleep(1000);
                    Vibration.Vibrate();
                }
                catch (Exception ex)
                {
                    // Other error has occurred.
                }

            }
        }

        public void OnTagDiscovered(Tag tag)
        {
            try
            {
                var techs = tag.GetTechList();
                if (!techs.Contains(Java.Lang.Class.FromType(typeof(MifareUltralight)).Name))
                    return;

             //   var ndef = Ndef.Get(tag);
             //   ndef.Connect();
             //   var ndefMessage = ndef.NdefMessage;
             //   var records = ndefMessage.GetRecords();
             //   ndef.Close();
                
            }
            catch
            {
                // handle errors
            }
        }
    }
}