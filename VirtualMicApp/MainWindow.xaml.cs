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
        private System.Windows.Controls.ComboBox audioSourceComboBox;
        private System.Windows.Controls.CheckBox playbackCheckBox;

        public MainWindow()
        {
            InitializeComponent();
            LoadAudioDevices();
        }

        private void LoadAudioDevices()
        {
            for (int i = 0; i < WaveOut.DeviceCount; i++)
            {
                var capabilities = WaveOut.GetCapabilities(i);
                audioSourceComboBox.Items.Add($"{i} - {capabilities.ProductName}");
            }
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
                var selectedIndex = int.Parse(selectedText.Split('-')[0].Trim());

                capture = new WasapiLoopbackCapture();
                capture.DataAvailable += Capture_DataAvailable;

                bufferedWaveProvider = new BufferedWaveProvider(capture.WaveFormat);

                int vbCableDeviceNumber = FindVBCableDeviceIndex();
                waveOutToVirtualDevice = new WaveOutEvent
                {
                    DeviceNumber = vbCableDeviceNumber
                };
                waveOutToVirtualDevice.Init(bufferedWaveProvider);

                if (playbackCheckBox.IsChecked == true)
                {
                    waveOutToSpeakers = new WaveOutEvent
                    {
                        DeviceNumber = selectedIndex
                    };
                    waveOutToSpeakers.Init(bufferedWaveProvider);
                    waveOutToSpeakers.Play(); 
                }

                capture.StartRecording();
                waveOutToVirtualDevice.Play();

                StartButton.IsEnabled = false;
                StopButton.IsEnabled = true;
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

                StartButton.IsEnabled = true;
                StopButton.IsEnabled = false;
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
            return -1;
        }
    }
}
// i added output thingy because im too cool and if you got any errors, will be easier to fix later.
