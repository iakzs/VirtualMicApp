using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Linq;
using System.Windows;

namespace VirtualMicApp
{
    public partial class MainWindow : Window
    {
        private WasapiLoopbackCapture capture;
        private WaveOutEvent waveOut;

        public MainWindow()
        {
            InitializeComponent();
            LoadAudioDevices();
        }

        private void LoadAudioDevices()
        {
            var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            audioSourceComboBox.ItemsSource = devices.Select(d => d.FriendlyName).ToList();
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            try {
                var selectedDeviceName = audioSourceComboBox.SelectedItem.ToString();
                var enumerator = new MMDeviceEnumerator();
                var device = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                                   .FirstOrDefault(d => d.FriendlyName == selectedDeviceName);

                if (device != null)
                {
                    capture = new WasapiLoopbackCapture(device);
                    capture.DataAvailable += Capture_DataAvailable;

                    waveOut = new WaveOutEvent
                    {
                        DeviceNumber = -1 // -1 selects the default output device, change to VB-Audio device index
                    };

                    capture.StartRecording();
                    startButton.IsEnabled = false;
                    stopButton.IsEnabled = true;
                }
                else
                {
                    MessageBox.Show("Audio device not found.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while starting: {ex.Message}. Please inform koslz at: @iakzs:matrix.org");
            }
        }   

        private void Capture_DataAvailable(object sender, WaveInEventArgs e)
        {
            // Send the captured audio data to the VB-Audio device
            waveOut.Init(new RawSourceWaveStream(e.Buffer, 0, e.BytesRecorded, capture.WaveFormat));
            waveOut.Play();
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                capture.StopRecording();
                waveOut.Stop();
                startButton.IsEnabled = true;
                stopButton.IsEnabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while stopping: {ex.Message}. Please inform koslz at: @iakzs:matrix.org");
            }
        }
    }
}
