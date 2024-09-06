using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Linq;
using System.Windows;

namespace VirtualMicApp
{
    public partial class MainWindow : Window
    {
        private WasapiLoopbackCapture? capture;
        private WaveOutEvent? waveOutToVirtualDevice;
        private WaveOutEvent? waveOutToSpeakers;
        private BufferedWaveProvider? bufferedWaveProvider;

        public MainWindow()
        {
            InitializeComponent();
            LoadAudioDevices();
        }

        private void LoadAudioDevices()
        {
            var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
        
            foreach (var device in devices)
            {
                audioSourceComboBox.Items.Add($"Mic - {device.FriendlyName}");
            }

            for (int i = 0; i < WaveOut.DeviceCount; i++)
            {
                var capabilities = WaveOut.GetCapabilities(i);
                audioSourceComboBox.Items.Add($"Speaker - {i} - {capabilities.ProductName}");
            }
        }
        
        private void StartRealMicrophone(string selectedText)
        {
            var micDeviceName = selectedText.Replace("Mic - ", "");
            var enumerator = new MMDeviceEnumerator();
            var device = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                                  .FirstOrDefault(d => d.FriendlyName == micDeviceName);
        
            if (device == null)
            {
                MessageBox.Show("Microphone not found.");
                return;
            }
        
            capture = new WasapiCapture(device);
            capture.DataAvailable += Capture_DataAvailable;
        
            bufferedWaveProvider = new BufferedWaveProvider(capture.WaveFormat);

            waveOutToVirtualDevice = new WaveOutEvent();
            waveOutToVirtualDevice.Init(bufferedWaveProvider);
            waveOutToVirtualDevice.Play();
        
            capture.StartRecording();
        }
        private void StartSpeakerMic(string selectedText)
        {
            var selectedIndex = int.Parse(selectedText.Split('-')[1].Trim());
        
            capture = new WasapiLoopbackCapture();
            capture.DataAvailable += Capture_DataAvailable;
        
            bufferedWaveProvider = new BufferedWaveProvider(capture.WaveFormat);
            
            waveOutToVirtualDevice = new WaveOutEvent();
            waveOutToVirtualDevice.DeviceNumber = FindVBCableDeviceIndex();
            waveOutToVirtualDevice.Init(bufferedWaveProvider);
            waveOutToVirtualDevice.Play();
        
            capture.StartRecording();
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (audioSourceComboBox.SelectedItem == null)
            {
                MessageBox.Show("Please select an audio device.");
                return;
            }
        
            try
            {
                var selectedText = audioSourceComboBox.SelectedItem.ToString();
        
                if (selectedText.StartsWith("Mic -"))
                {
                    StartRealMicrophone(selectedText);
                }
                else if (selectedText.StartsWith("Speaker -"))
                {
                    StartSpeakerMic(selectedText);
                }
        
                startButton.IsEnabled = false;
                stopButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while starting: {ex.Message}. Please inform koslz at: @iakzs:matrix.org");
            }
        }


        private void Capture_DataAvailable(object? sender, WaveInEventArgs e)
        {
            bufferedWaveProvider?.AddSamples(e.Buffer, 0, e.BytesRecorded);
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                capture?.StopRecording();
                capture?.Dispose();

                waveOutToSpeakers?.Stop();
                waveOutToSpeakers?.Dispose();

                waveOutToVirtualDevice?.Stop();
                waveOutToVirtualDevice?.Dispose();

                startButton.IsEnabled = true;
                stopButton.IsEnabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while stopping: {ex.Message}. Please inform koslz at: @iakzs:matrix.org");
            }
        }

        private int FindVBCableDeviceIndex()
        {
            for (int i = 0; i < WaveOut.DeviceCount; i++)
            {
                var capabilities = WaveOut.GetCapabilities(i);
                if (capabilities.ProductName.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            MessageBox.Show("VB-Audio Cable device not found.");
            return -1;
        }        
    }
}
// i added output thingy because im too cool and if you got any errors, will be easier to fix later.
