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
        }

        private void LoadAudioDevices()
        {
            var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            audioSourceComboBox.ItemsSource = devices.Select(d => d.FriendlyName).ToList();
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
        
                var enumerator = new MMDeviceEnumerator();
                var device = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                                       .FirstOrDefault(d => d.FriendlyName == selectedDeviceName);
        
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
                    StartMicrophoneCapture();
        
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

        private void StartMicrophoneCapture()
        {
            micCapture = new WaveInEvent
            {
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
