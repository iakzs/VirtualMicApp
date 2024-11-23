using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Collections.Generic;

namespace VirtualMicApp
{
    public partial class MainWindow : Window
    {
        private WasapiLoopbackCapture? capture;
        private WaveIn? microphoneInput;
        private WaveOutEvent? waveOutToVirtualDevice;
        private WaveOutEvent? waveOutToSpeakers;
        private BufferedWaveProvider? bufferedWaveProvider;
        private BufferedWaveProvider? microphoneBuffer;
        private MixingSampleProvider? mixer;
        private float audioSourceGain = 1.0f;
        private float microphoneGain = 1.0f;

        public MainWindow()
        {
            InitializeComponent();
            LoadAudioDevices();
            LoadMicrophoneDevices();
            LoadEffects();
            SetupVolumeControls();
        }

        private void LoadAudioDevices()
        {
            audioSourceComboBox.Items.Clear();
            for (int i = 0; i < WaveOut.DeviceCount; i++)
            {
                var capabilities = WaveOut.GetCapabilities(i);
                audioSourceComboBox.Items.Add($"{i} - {capabilities.ProductName}");
            }
            if (audioSourceComboBox.Items.Count > 0)
                audioSourceComboBox.SelectedIndex = 0;
        }

        private void LoadMicrophoneDevices()
        {
            microphoneComboBox.Items.Clear();
            for (int i = 0; i < WaveIn.DeviceCount; i++)
            {
                var capabilities = WaveIn.GetCapabilities(i);
                microphoneComboBox.Items.Add($"{i} - {capabilities.ProductName}");
            }
            if (microphoneComboBox.Items.Count > 0)
                microphoneComboBox.SelectedIndex = 0;
        }

        private void LoadEffects()
        {
            effectsComboBox.Items.Add("None");
            effectsComboBox.Items.Add("Echo");
            effectsComboBox.Items.Add("Reverb");
            effectsComboBox.Items.Add("Low Quality Mic");
            effectsComboBox.SelectedIndex = 0;
        }

        private void SetupVolumeControls()
        {
            audioSourceVolume.ValueChanged += (s, e) => audioSourceGain = (float)e.NewValue;
            microphoneVolume.ValueChanged += (s, e) => microphoneGain = (float)e.NewValue;
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedText = audioSourceComboBox.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(selectedText))
            {
                MessageBox.Show("Please select an audio device.");
                return;
            }

            try
            {
                var selectedIndex = int.Parse(selectedText.Split('-')[0].Trim());

                capture = new WasapiLoopbackCapture();
                var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
                capture.WaveFormat = waveFormat;

                bufferedWaveProvider = new BufferedWaveProvider(waveFormat)
                {
                    DiscardOnBufferOverflow = false,
                    BufferLength = waveFormat.AverageBytesPerSecond / 2
                };

                capture.DataAvailable += (s, a) =>
                {
                    if (bufferedWaveProvider != null && 
                        bufferedWaveProvider.BufferedBytes < bufferedWaveProvider.BufferLength - a.BytesRecorded)
                    {
                        bufferedWaveProvider.AddSamples(a.Buffer, 0, a.BytesRecorded);
                    }
                };

                var sources = new List<ISampleProvider>();
                var audioSourceProvider = new SampleChannel(bufferedWaveProvider, true);
                audioSourceProvider.Volume = audioSourceGain;
                sources.Add(audioSourceProvider);

                if (enableMicrophoneCheckBox.IsChecked == true)
                {
                    var micSelectedText = microphoneComboBox.SelectedItem?.ToString();
                    if (string.IsNullOrEmpty(micSelectedText))
                    {
                        MessageBox.Show("Please select a microphone device.");
                        return;
                    }

                    var micSelectedIndex = int.Parse(micSelectedText.Split('-')[0].Trim());
                    microphoneInput = new WaveIn
                    {
                        DeviceNumber = micSelectedIndex,
                        WaveFormat = waveFormat,
                        BufferMilliseconds = 50
                    };

                    microphoneBuffer = new BufferedWaveProvider(waveFormat)
                    {
                        DiscardOnBufferOverflow = false,
                        BufferLength = waveFormat.AverageBytesPerSecond / 4
                    };

                    microphoneInput.DataAvailable += (s, a) =>
                    {
                        if (microphoneBuffer != null && 
                            microphoneBuffer.BufferedBytes < microphoneBuffer.BufferLength - a.BytesRecorded)
                        {
                            microphoneBuffer.AddSamples(a.Buffer, 0, a.BytesRecorded);
                        }
                    };

                    var micChannel = new SampleChannel(microphoneBuffer, true);
                    micChannel.Volume = microphoneGain;
                    sources.Add(micChannel);
                }

                mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
                foreach (var source in sources)
                {
                    mixer.AddMixerInput(source);
                }

                ISampleProvider finalOutput = mixer;
                var effectType = effectsComboBox.SelectedItem?.ToString() ?? "None";
                if (effectType != "None")
                {
                    switch (effectType)
                    {
                        case "Echo":
                            finalOutput = new EchoEffect(mixer)
                            {
                                Delay = (int)(effectParam1.Value * 1000),
                                Decay = (float)effectParam2.Value
                            };
                            break;
                        case "Reverb":
                            var delay1 = new EchoEffect(mixer) { Delay = 50, Decay = 0.3f };
                            finalOutput = new EchoEffect(delay1) { Delay = 100, Decay = 0.2f };
                            break;
                        case "Low Quality Mic":
                            finalOutput = new LowQualityMicEffect(mixer)
                            {
                                BitReduction = (int)(effectParam1.Value * 6) + 2,
                                NoiseLevel = (float)effectParam2.Value * 0.1f
                            };
                            break;
                    }
                }

                var vbCableOutput = finalOutput.ToWaveProvider();
                var speakerOutput = finalOutput.ToWaveProvider();

                int vbCableDeviceNumber = FindVBCableDeviceIndex();
                if (vbCableDeviceNumber != -1)
                {
                    waveOutToVirtualDevice = new WaveOutEvent { DeviceNumber = vbCableDeviceNumber };
                    waveOutToVirtualDevice.Init(vbCableOutput);
                    waveOutToVirtualDevice.Play();
                    Dispatcher.Invoke(() => debugOutput.Text = $"Started VB-Cable output on device {vbCableDeviceNumber}");
                }
                else
                {
                    MessageBox.Show("VB-Cable not found. Please install VB-Cable.");
                    return;
                }

                if (playbackCheckBox.IsChecked ?? false)
                {
                    try 
                    {
                        waveOutToSpeakers = new WaveOutEvent { DeviceNumber = -1 }; // -1 for default output device
                        waveOutToSpeakers.Init(speakerOutput);
                        waveOutToSpeakers.Play();
                        Dispatcher.Invoke(() => debugOutput.Text += "\nStarted speaker output");
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() => debugOutput.Text += $"\nError starting speaker output: {ex.Message}");
                    }
                }

                capture.StartRecording();
                Dispatcher.Invoke(() => debugOutput.Text += "\nStarted audio capture");

                if (microphoneInput != null)
                {
                    microphoneInput.StartRecording();
                    Dispatcher.Invoke(() => debugOutput.Text += "\nStarted microphone capture");
                }

                StartButton.IsEnabled = false;
                StopButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while starting: {ex.Message}. Please inform koslz at: @iakzs:matrix.org");
                StopAndCleanup();
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (capture != null)
                {
                    capture.StopRecording();
                    capture.Dispose();
                    capture = null;
                }

                if (microphoneInput != null)
                {
                    microphoneInput.StopRecording();
                    microphoneInput.Dispose();
                    microphoneInput = null;
                }

                if (waveOutToVirtualDevice != null)
                {
                    waveOutToVirtualDevice.Stop();
                    waveOutToVirtualDevice.Dispose();
                    waveOutToVirtualDevice = null;
                }

                if (waveOutToSpeakers != null)
                {
                    waveOutToSpeakers.Stop();
                    waveOutToSpeakers.Dispose();
                    waveOutToSpeakers = null;
                }

                bufferedWaveProvider = null;
                microphoneBuffer = null;
                mixer = null;

                StartButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                Dispatcher.Invoke(() => debugOutput.Text = "Stopped all audio processing");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error stopping audio: {ex.Message}");
            }
        }

        private void StopAndCleanup()
        {
            try
            {
                capture?.StopRecording();
                capture?.Dispose();
                capture = null;

                microphoneInput?.StopRecording();
                microphoneInput?.Dispose();
                microphoneInput = null;

                waveOutToSpeakers?.Stop();
                waveOutToSpeakers?.Dispose();
                waveOutToSpeakers = null;

                waveOutToVirtualDevice?.Stop();
                waveOutToVirtualDevice?.Dispose();
                waveOutToVirtualDevice = null;

                bufferedWaveProvider = null;
                microphoneBuffer = null;
                mixer = null;

                StartButton.IsEnabled = true;
                StopButton.IsEnabled = false;

                Dispatcher.Invoke(() => debugOutput.Text = "Stopped all audio processing");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during cleanup: {ex.Message}");
            }
        }

        private int FindVBCableDeviceIndex()
        {
            for (int i = 0; i < WaveOut.DeviceCount; i++)
            {
                var capabilities = WaveOut.GetCapabilities(i);
                if (capabilities.ProductName.Contains("CABLE", StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return -1;
        }
    }

    public class EchoEffect : ISampleProvider
    {
        private readonly ISampleProvider source;
        private readonly Queue<float> delayBuffer;
        private int delay = 1000;
        private float decay = 0.5f;

        public EchoEffect(ISampleProvider source)
        {
            this.source = source;
            this.delayBuffer = new Queue<float>();
        }

        public int Delay
        {
            get => delay;
            set
            {
                delay = value;
                while (delayBuffer.Count > delay)
                    delayBuffer.Dequeue();
            }
        }

        public float Decay
        {
            get => decay;
            set => decay = Math.Max(0, Math.Min(1, value));
        }

        public WaveFormat WaveFormat => source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = source.Read(buffer, offset, count);

            for (int i = 0; i < samplesRead; i++)
            {
                float echo = delayBuffer.Count >= delay ? delayBuffer.Dequeue() * decay : 0;
                float newSample = buffer[offset + i] + echo;
                delayBuffer.Enqueue(buffer[offset + i]);
                buffer[offset + i] = newSample;
            }

            return samplesRead;
        }
    }

    public class LowQualityMicEffect : ISampleProvider
    {
        private readonly ISampleProvider source;
        private readonly Random random;
        private int bitReduction = 4;
        private float noiseLevel = 0.05f;
        private int sampleCounter = 0;
        private float lastSample = 0f;

        public LowQualityMicEffect(ISampleProvider source)
        {
            this.source = source;
            this.random = new Random();
        }

        public int BitReduction
        {
            get => bitReduction;
            set => bitReduction = Math.Clamp(value, 1, 16);
        }

        public float NoiseLevel
        {
            get => noiseLevel;
            set => noiseLevel = Math.Clamp(value, 0f, 1f);
        }

        public WaveFormat WaveFormat => source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = source.Read(buffer, offset, count);

            float levels = (float)Math.Pow(2, BitReduction);

            for (int i = 0; i < samplesRead; i++)
            {
                float noise = ((float)random.NextDouble() * 2 - 1) * NoiseLevel;
                float sample = buffer[offset + i];
                sample += noise;
                sample = (float)Math.Round(sample * levels) / levels;

                sampleCounter++;
                if (sampleCounter % 2 == 0)
                {
                    sample = lastSample;
                }
                lastSample = sample;

                sample = Math.Clamp(sample * 1.2f, -1f, 1f);
                buffer[offset + i] = sample;
            }

            return samplesRead;
        }
    }

    public static class AudioExtensions
    {
        public static IWaveProvider ToWaveProvider(this ISampleProvider provider)
        {
            return new SampleToWaveProvider(provider);
        }
    }
}
