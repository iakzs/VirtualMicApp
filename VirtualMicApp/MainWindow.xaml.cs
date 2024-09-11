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
        private WaveInEvent? micCapture;
        private WaveOutEvent? waveOutToVirtualDevice;
        private WaveOutEvent? waveOutToSpeakers;
        private BufferedWaveProvider? bufferedWaveProvider;
        private BufferedWaveProvider? micBufferedWaveProvider;

        public MainWindow()
        {
            InitializeComponent();
            LoadAudioDevices();
            LoadMicrophoneDevices();
        }

        private void LoadAudioDevices()
        {
            for (int i = 0; i < WaveOut.DeviceCount; i++)
            {
                var capabilities = WaveOut.GetCapabilities(i);
                audioSourceComboBox.Items.Add($"{i} - {capabilities.ProductName}");
            }
        }

        private void LoadMicrophoneDevices()
        {
            var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            microphoneComboBox.ItemsSource = devices.Select(d => d.FriendlyName).ToList();
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
                var selectedAudioText = audioSourceComboBox.SelectedItem.ToString();
                var selectedAudioIndex = int.Parse(selectedAudioText.Split('-')[0].Trim());

                bufferedWaveProvider = new BufferedWaveProvider(new WaveFormat());

                if (microphoneComboBox.SelectedItem != null)
                {
                    var selectedMicName = microphoneComboBox.SelectedItem.ToString();
                    var enumerator = new MMDeviceEnumerator();
                    var micDevice = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                                              .FirstOrDefault(d => d.FriendlyName == selectedMicName);

                    if (micDevice != null)
                    {
                        micCapture = new WaveInEvent
                        {
                            DeviceNumber = Array.IndexOf(enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToArray(), micDevice),
                            WaveFormat = new WaveFormat(44100, 1) // Configure as needed
                        };
                        micCapture.DataAvailable += MicCapture_DataAvailable;
                        micBufferedWaveProvider = new BufferedWaveProvider(micCapture.WaveFormat);

                        micCapture.StartRecording();
                    }
                }

                capture = new WasapiLoopbackCapture();
                capture.DataAvailable += Capture_DataAvailable;

                waveOutToVirtualDevice = new WaveOutEvent
                {
                    DeviceNumber = FindVBCableDeviceIndex()
                };

                if (micBufferedWaveProvider != null)
                {
                    waveOutToVirtualDevice.Init(new MultiplexingWaveProvider(new IWaveProvider[] { bufferedWaveProvider, micBufferedWaveProvider }, 2));
                }
                else
                {
                    waveOutToVirtualDevice.Init(bufferedWaveProvider);
                }

                if (playbackCheckBox.IsChecked == true)
                {
                    waveOutToSpeakers = new WaveOutEvent
                    {
                        DeviceNumber = selectedAudioIndex
                    };
                    waveOutToSpeakers.Init(bufferedWaveProvider);
                    waveOutToSpeakers.Play();
                }

                capture.StartRecording();
                waveOutToVirtualDevice.Play();

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

        private void MicCapture_DataAvailable(object? sender, WaveInEventArgs e)
        {
            micBufferedWaveProvider?.AddSamples(e.Buffer, 0, e.BytesRecorded);
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                capture?.StopRecording();
                capture?.Dispose();

                micCapture?.StopRecording();
                micCapture?.Dispose();

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
            return -1;
        }
    }
}
