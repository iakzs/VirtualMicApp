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
        private WaveInEvent micCapture;
        private WaveOutEvent waveOut;
        private BufferedWaveProvider bufferedWaveProvider;

        public MainWindow()
        {
            InitializeComponent();
            LoadAudioDevices();
            LoadMicrophoneDevices();
        }

        private void LoadAudioDevices()
        {
            var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            audioSourceComboBox.ItemsSource = devices.Select(d => d.FriendlyName).ToList();
        }

        private void LoadMicrophoneDevices()
        {
            var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            microphoneComboBox.ItemsSource = devices.Select(d => d.FriendlyName).ToList();
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedDeviceName = audioSourceComboBox.SelectedItem?.ToString();
                if (string.IsNullOrEmpty(selectedDeviceName))
                {
                    MessageBox.Show("Please select an audio device.");
                    return;
                }

                var selectedMicName = microphoneComboBox.SelectedItem?.ToString();

                var enumerator = new MMDeviceEnumerator();
                var device = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                                       .FirstOrDefault(d => d.FriendlyName == selectedDeviceName);

                MMDevice micDevice = null;
                if (!string.IsNullOrEmpty(selectedMicName))
                {
                    micDevice = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                                          .FirstOrDefault(d => d.FriendlyName == selectedMicName);
                }

                if (device != null)
                {
                    capture = new WasapiLoopbackCapture(device);
                    capture.DataAvailable += Capture_DataAvailable;

                    bufferedWaveProvider = new BufferedWaveProvider(capture.WaveFormat);

                    if (playbackCheckBox.IsChecked == true)
                    {
                        waveOut = new WaveOutEvent
                        {
                            DeviceNumber = -1
                        };

                        waveOut.Init(bufferedWaveProvider);
                        waveOut.Play();
                    }

                    capture.StartRecording();

                    if (micDevice != null)
                    {
                        StartMicrophoneCapture(micDevice);
                    }

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
            bufferedWaveProvider?.AddSamples(e.Buffer, 0, e.BytesRecorded);
        }

        private void StartMicrophoneCapture(MMDevice micDevice)
        {
            micCapture = new WaveInEvent
            {
                DeviceNumber = Array.IndexOf(new MMDeviceEnumerator().EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToArray(), micDevice),
                WaveFormat = new WaveFormat(44100, 1)
            };
            micCapture.DataAvailable += MicCapture_DataAvailable;
            micCapture.StartRecording();
        }

        private void MicCapture_DataAvailable(object sender, WaveInEventArgs e)
        {
            bufferedWaveProvider?.AddSamples(e.Buffer, 0, e.BytesRecorded);
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                capture?.StopRecording();
                micCapture?.StopRecording();

                waveOut?.Stop();
                waveOut?.Dispose();

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
