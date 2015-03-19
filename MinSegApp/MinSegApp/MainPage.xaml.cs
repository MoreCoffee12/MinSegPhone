﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Networking.Sockets;
using Windows.Networking.Proximity;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.UI;
using VisualizationTools;
using DataBus;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=391641

namespace MinSegApp
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();

            this.NavigationCacheMode = NavigationCacheMode.Required;

            // Create our LineGraph and hook the up to their respective images
            graphPosition = new VisualizationTools.LineGraph((int)LineGraphPosition.ActualWidth, (int)LineGraphPosition.Height);
            LineGraphPosition.Source = graphPosition;

            graphVelocity = new VisualizationTools.LineGraph((int)LineGraphVelocity.ActualWidth, (int)LineGraphVelocity.Height);
            LineGraphVelocity.Source = graphVelocity;

            graphPitchAngle = new VisualizationTools.LineGraph((int)LineGraphPitchAngle.ActualWidth, (int)LineGraphPitchAngle.Height);
            LineGraphPitchAngle.Source = graphPitchAngle;

            graphPitchAngleRate_Avg = new VisualizationTools.LineGraph((int)LineGraphPitchAngleRate_Avg.ActualWidth, (int)LineGraphPitchAngleRate_Avg.Height);
            LineGraphPitchAngleRate_Avg.Source = graphPitchAngleRate_Avg;

            graphControllerEffort = new VisualizationTools.LineGraph((int)LineGraphControllerEffort.ActualWidth, (int)LineGraphControllerEffort.Height);
            LineGraphControllerEffort.Source = graphControllerEffort;

            // Initialize the buffers that recieve the data from the Arduino
            bCollectData = false;
            iChannelCount = 8;
            iBuffLength = 1024;
            iBuffStart = -1;
            iBuffData = new int[iChannelCount * iBuffLength];
            Array.Clear(iBuffData, 0, iBuffData.Length);
            idxData = 0;
            cBuff = new byte[iBuffLength];
            Array.Clear(cBuff, 0, cBuff.Length);
            iPreLoad = 48;
            idxData = 0;

            // Data buffers
            dataPosition = new float[iBuffLength];
            dataVelocity = new float[iBuffLength];
            dataPitchAngle = new float[iBuffLength];
            dataPitchAngleRate_Avg = new float[iBuffLength];
            dataVolts = new float[iBuffLength];
            dataPitchAngleRate_Upper = new float[iBuffLength];
            dataPitchAngleRate_Lower = new float[iBuffLength];
            dataSpare = new float[iBuffLength];

        }

        /// <summary>
        /// Invoked when this page is about to be displayed in a Frame.
        /// </summary>
        /// <param name="e">Event data that describes how this page was reached.
        /// This parameter is typically used to configure the page.</param>
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {

            float fOffset = 0.0f;

            // Retrieve the phone theme settings so that the plots can 
            // be tailored to match
            Color colorCurrentBackground = (Color)Application.Current.Resources["PhoneBackgroundColor"];
            float fR = Convert.ToSingle(colorCurrentBackground.R) / 255.0f;
            float fG = Convert.ToSingle(colorCurrentBackground.G) / 255.0f;
            float fB = Convert.ToSingle(colorCurrentBackground.B) / 255.0f;
            if (fR + fG + fB < 1.5)
            {
                fOffset = 0.5f;
            }

            // Initialize the buffer for the frame timebase and set the color
            graphPosition.setColor(0.0f, 0.5f + fOffset, 0.0f);
            graphPosition.setColorBackground(fR, fG, fB, 0.0f);
            graphVelocity.setColor(0.0f, 0.5f + fOffset, 0.0f);
            graphVelocity.setColorBackground(fR, fG, fB, 0.0f);
            graphPitchAngle.setColor(0.0f, 0.5f + fOffset, 0.0f);
            graphPitchAngle.setColorBackground(fR, fG, fB, 0.0f);
            graphPitchAngleRate_Avg.setColor(0.0f, 0.5f + fOffset, 0.0f);
            graphPitchAngleRate_Avg.setColorBackground(fR, fG, fB, 0.0f);
            graphControllerEffort.setColor(0.0f, 0.5f + fOffset, 0.0f);
            graphControllerEffort.setColorBackground(fR, fG, fB, 0.0f);

            // Configure the line plots scaling
            graphPosition.setYLim(-1.0f, 1.0f);
            graphVelocity.setYLim(-0.1f, 0.1f);
            graphPitchAngle.setYLim(-0.02f, 0.02f);
            graphPitchAngleRate_Avg.setYLim(-1.0f, 1.0f);
            graphControllerEffort.setYLim(-9.0f, 9.0f);

            // Also hook the Rendering cycle up to the CompositionTarget Rendering event so we draw frames when we're supposed to
            CompositionTarget.Rendering += graphPosition.Render;
            CompositionTarget.Rendering += graphVelocity.Render;
            CompositionTarget.Rendering += graphPitchAngle.Render;
            CompositionTarget.Rendering += graphPitchAngleRate_Avg.Render;
            CompositionTarget.Rendering += graphControllerEffort.Render;

            // Arduino bluetooth
            try
            {
                ReadData(SetupBluetoothLink());
            }
            catch (Exception ex)
            {
                this.textOutput.Text = "Exception:  " + ex.ToString();
            }



        }

        #region private methods

        private async Task<bool> SetupBluetoothLink()
        {
            try
            {
                // Reset the OK indicator
                rectFrameOK.Fill = new SolidColorBrush(Colors.Black);
                rectBTOK.Fill = new SolidColorBrush(Colors.Black);

                // Tell PeerFinder that we're a pair to anyone that has been paried with us over BT
                PeerFinder.AlternateIdentities["Bluetooth:PAIRED"] = "";

                // Find all peers
                var devices = await PeerFinder.FindAllPeersAsync();

                // If there are no peers, then complain
                if (devices.Count == 0)
                {
                    await new MessageDialog("No bluetooth devices are paired, please pair your Arduino").ShowAsync();

                    // Neat little line to open the bluetooth settings
                    await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings-bluetooth:"));
                    return false;
                }
                this.textOutput.Text = "Found paired bluetooth devices\n";

                // Convert peers to array from strange datatype return from PeerFinder.FindAllPeersAsync()
                PeerInformation[] peers = devices.ToArray();

                // Find paired peer that is the FoneAstra.  Modified to look for the default device
                // name for the Arduino
                //PeerInformation peerInfo = devices.FirstOrDefault(c => c.DisplayName.Contains("FoneAstra"));
                PeerInformation peerInfo = devices.FirstOrDefault(c => c.DisplayName.Contains("HC"));

                // If that doesn't exist, complain!
                if (peerInfo == null)
                {
                    //await new MessageDialog("No bluetooth devices are paired, please pair your FoneAstra").ShowAsync();
                    await new MessageDialog("No bluetooth devices are paired, please pair your HC").ShowAsync();
                    await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings-bluetooth:"));
                    return false;
                }
                this.textOutput.Text += ("Found the HC bluetooth device:" + peerInfo.HostName + "\n");

                // Otherwise, create our StreamSocket and connect it!
                s = new StreamSocket();
                try
                {
                    await s.ConnectAsync(peerInfo.HostName, "1");
                }
                catch (Exception ex)
                {
                    this.textOutput.Text = "ConnectAsync Failed\n";
                    this.textOutput.Text += "Exception:  " + ex.ToString();
                    return false;
                }

                // At this point, open the writer
                //output = new DataWriter(s.OutputStream);
                return true;


            }
            catch (Exception ex)
            {
                this.textOutput.Text = ("Error: " + ex.HResult.ToString() + " - " + ex.Message + "\n");
                this.textOutput.Text += "Is your bluetooth turned on?\n";
                return false;
            }

            return true;
        }

        // Read in iBuffLength number of samples
        private async Task<bool> bReadSource(DataReader input)
        {
            uint k;
            UInt16 idxChannel;
            UInt16 idxFrame;
            UInt16 i;
            UInt16 iStoredCRC;
            int iBuffEnd;
            byte iFrameSize;
            UInt16 crc;
            mbus = new MinSegBus();
            byte[] cFrame;

            // Read in the data
            for (k = 0; k < iBuffLength; k++)
            {


                // Read in one byte
                try
                {
                    // Wait until we have 1 byte available to read
                    await input.LoadAsync(1);

                    // Read in the byte
                    cBuff[k] = input.ReadByte();

                }
                catch (Exception ex)
                {
                    this.textOutput.Text = "Exception:  " + ex.ToString();
                }

                // Construct the byte array, look for the beginning byte
                if (cBuff[(k - iPreLoad) % iBuffLength] == 0 && cBuff[(k - iPreLoad + 1) % iBuffLength] == 0 && iBuffStart < 0)
                {

                    // This could be the start of a frame or it could just be a zero
                    // value passed in.  The following sequence checks that it is a
                    // valid data frame.
                    iFrameSize = cBuff[(k - iPreLoad + 2) % iBuffLength];

                    // The frame size is bounded to 256 bytes and must be at last 11
                    if (iFrameSize < iPreLoad && iFrameSize > 11 && k < (iBuffLength - (uint)iFrameSize))
                    {

                        // This is beginning and ending of the region of interest
                        iBuffStart = Convert.ToInt32((k - iPreLoad) % iBuffLength);
                        iBuffEnd = (iBuffStart + iFrameSize - 1) % Convert.ToInt16(iBuffLength);

                        // extract the possible frame including the end mark zeros. 
                        // There is some logic here to handle the wrapping
                        if (iBuffStart < iBuffEnd)
                        {

                            cFrame = new byte[iBuffEnd - iBuffStart];
                            Array.Copy(cBuff, iBuffStart, cFrame, 0, (iBuffEnd - iBuffStart));

                        }
                        else
                        {
                            int iFrameLength = iBuffEnd + (Convert.ToInt32(iBuffLength) - iBuffStart) + 1;
                            cFrame = new byte[iFrameLength];
                            Array.Copy(cBuff, iBuffStart, cFrame, 0, (Convert.ToInt32(iBuffLength) - iBuffStart));
                            Array.Copy(cBuff, 0, cFrame, (Convert.ToInt32(iBuffLength) - iBuffStart), iBuffEnd + 1);
                        }

                        // Check crc
                        crc = 0xFFFF;
                        for (i = 0; i < (iFrameSize - 4); i++)
                        {
                            crc = mbus.bUpdateCRC(crc, cFrame[i]);
                        }

                        // Compare with the recorded crc
                        iStoredCRC = BitConverter.ToUInt16(cFrame, iFrameSize - 4);
                        if (crc == iStoredCRC)
                        {
                            rectFrameOK.Fill = new SolidColorBrush(Colors.Green);
                        }

                        // Check that this is 32-bit floating point value
                        if (cFrame[4] == 0x00)
                        {
                            // Reset the channel count
                            idxChannel = 0;

                            // Point to the next location in the buffer
                            if (idxData < (iBuffLength - 1))
                            {
                                idxData++;
                            }
                            else
                            {
                                idxData = 0;
                                //Array.Clear(iBuffData, 0, iBuffData.Length);
                            }

                            // Fill the spaces in the buffer with data
                            dataPosition[idxData] = BitConverter.ToSingle(cFrame, 5);
                            dataVelocity[idxData] = BitConverter.ToSingle(cFrame, 9);
                            dataPitchAngle[idxData] = BitConverter.ToSingle(cFrame, 13);
                            dataPitchAngleRate_Avg[idxData] = BitConverter.ToSingle(cFrame, 17);
                            dataVolts[idxData] = BitConverter.ToSingle(cFrame, 21);
                            dataPitchAngleRate_Upper[idxData] = BitConverter.ToSingle(cFrame, 25);
                            dataPitchAngleRate_Lower[idxData] = BitConverter.ToSingle(cFrame, 29);
                            dataSpare[idxData] = BitConverter.ToSingle(cFrame, 33);

                            // Debugging ->
                            //for (idxFrame = 0; idxFrame < (iFrameSize - 9); idxFrame = Convert.ToUInt16(idxFrame + 2))
                            //{

                            //iBuffDataSet(idxChannel, idxData, BitConverter.ToUInt16(cFrame, idxFrame + 5));
                            //idxChannel++;
                            //}
                            //<- End debug code
                        }

                    }
                    else
                    {
                        rectFrameOK.Fill = new SolidColorBrush(Colors.Black);
                    }

                    // Reset the buffer pointer
                    iBuffStart = -1;

                }

            }

            // Success, return a true
            return true;
        }



        private async void ReadData(Task<bool> setupOK)
        {
            bool bTemp;

            // Wait for the setup function to finish, when it does, it returns a boolean
            // If the boolean is false, then something failed and we shouldn't attempt to read data
            try
            {
                if (!await setupOK)
                    return;
            }
            catch (Exception ex)
            {
                this.textOutput.Text = "Exception:  " + ex.ToString();
            }

            // Construct a dataReader so we can read junk in
            DataReader input = new DataReader(s.InputStream);

            // Made it this far so status is ok
            rectBTOK.Fill = new SolidColorBrush(Colors.Green);

            // Loop forever
            while (true)
            {
                // Read a line from the input, once again using await to translate a "Task<xyz>" to an "xyz"
                bTemp = (await bReadSource(input));
                if (bTemp == true)
                {

                    // Append that line to our TextOutput
                    try
                    {
                        if (bCollectData)
                        {
                            // Update the plots
                            graphPosition.setArray(dataPosition);
                            graphVelocity.setArray(dataVelocity);
                            graphPitchAngle.setArray(dataPitchAngle);
                            graphPitchAngleRate_Avg.setArray(dataPitchAngleRate_Avg);
                            graphControllerEffort.setArray(dataVolts);
                        }

                    }
                    catch (Exception ex)
                    {
                        this.textOutput.Text = "Exception:  " + ex.ToString();
                    }
                }

            }

        }

        private void btnStartAcq_Click(object sender, RoutedEventArgs e)
        {

            // Toggle the data acquisition state and update the controls
            bCollectData = !bCollectData;

            if (bCollectData)
            {
                btnStartAcq.Content = "Stop Acquisition";
            }
            else
            {
                btnStartAcq.Content = "Start Acquisition";
            }

            bClearOutput = true;

        }

        #endregion

        #region private fields

        // The socket we'll use to send data to the the Aurduino
        private StreamSocket s;

        // graphs, gotta have the graphs
        private VisualizationTools.LineGraph graphPosition;
        private VisualizationTools.LineGraph graphVelocity;
        private VisualizationTools.LineGraph graphPitchAngle;
        private VisualizationTools.LineGraph graphPitchAngleRate_Avg;
        private VisualizationTools.LineGraph graphControllerEffort;
        private bool bClearOutput = false;
        private float[] dataPosition;
        private float[] dataVelocity;
        private float[] dataPitchAngle;
        private float[] dataPitchAngleRate_Avg;
        private float[] dataVolts;
        private float[] dataPitchAngleRate_Upper;
        private float[] dataPitchAngleRate_Lower;
        private float[] dataSpare;

        // Buffer and controls for the data from the instrumentation
        private bool bCollectData;
        uint iBuffLength;
        byte[] cBuff;
        uint iChannelCount;
        int iBuffStart;
        int[] iBuffData;
        uint idxData;

        // Data bus structures used to pull data off of the Arduino
        DataBus.MinSegBus mbus;
        uint iPreLoad;


        #endregion
    }


}
