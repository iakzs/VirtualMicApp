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
        private readonly int desiredLatency = 100;

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
        }

        private void LoadMicrophoneDevices()
        {
            microphoneComboBox.Items.Clear();
            for (int i = 0; i < WaveIn.DeviceCount; i++)
            {
                var capabilities = WaveIn.GetCapabilities(i);
                microphoneComboBox.Items.Add($"{i} - {capabilities.ProductName}");
            }
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
                var waveFormat = new WaveFormat(44100, 16, 2);

                capture = new WasapiLoopbackCapture();
                capture.WaveFormat = waveFormat;
                capture.DataAvailable += Capture_DataAvailable;
                bufferedWaveProvider = new BufferedWaveProvider(waveFormat)
                {
                    DiscardOnBufferOverflow = true,
                    BufferDuration = TimeSpan.FromMilliseconds(100)
                };

                var sources = new List<ISampleProvider>();
                sources.Add(new VolumeSampleProvider(bufferedWaveProvider.ToSampleProvider()) 
                { 
                    Volume = audioSourceGain 
                });

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
                        BufferMilliseconds = 20
                    };
                    microphoneInput.DataAvailable += Microphone_DataAvailable;
                    microphoneBuffer = new BufferedWaveProvider(waveFormat)
                    {
                        DiscardOnBufferOverflow = true,
                        BufferDuration = TimeSpan.FromMilliseconds(100)
                    };

                    sources.Add(new VolumeSampleProvider(microphoneBuffer.ToSampleProvider()) 
                    { 
                        Volume = microphoneGain 
                    });
                }

                mixer = new MixingSampleProvider(sources)
                {
                    ReadFully = true
                };

                ISampleProvider finalOutput;
                var effectType = effectsComboBox.SelectedItem?.ToString() ?? "None";
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
                    default:
                        finalOutput = mixer;
                        break;
                }

                int vbCableDeviceNumber = FindVBCableDeviceIndex();
                if (vbCableDeviceNumber != -1)
                {
                    waveOutToVirtualDevice = new WaveOutEvent
                    {
                        DeviceNumber = vbCableDeviceNumber,
                        DesiredLatency = 20
                    };
                    waveOutToVirtualDevice.Init(finalOutput.ToWaveProvider());
                    waveOutToVirtualDevice.Play();
                    Dispatcher.Invoke(() => debugOutput.Text = $"Started VB-Cable output on device {vbCableDeviceNumber}");
                }
                else
                {
                    MessageBox.Show("VB-Cable not found. Please install VB-Cable.");
                    return;
                }

                if (playbackCheckBox.IsChecked == true)
                {
                    waveOutToSpeakers = new WaveOutEvent
                    {
                        DeviceNumber = 0,
                        DesiredLatency = 20
                    };
                    waveOutToSpeakers.Init(finalOutput.ToWaveProvider());
                    waveOutToSpeakers.Play();
                    Dispatcher.Invoke(() => debugOutput.Text += "\nStarted speaker output");
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

        private void Capture_DataAvailable(object? sender, WaveInEventArgs e)
        {
            try
            {
                if (bufferedWaveProvider != null)
                {
                    if (bufferedWaveProvider.BufferedBytes > bufferedWaveProvider.WaveFormat.AverageBytesPerSecond * 2)
                    {
                        bufferedWaveProvider.ClearBuffer();
                        Dispatcher.Invoke(() => debugOutput.Text = "Audio source buffer cleared - overflow prevented");
                    }
                    bufferedWaveProvider.AddSamples(e.Buffer, 0, e.BytesRecorded);
                    
                    Dispatcher.Invoke(() => 
                    {
                        var bufferedMs = bufferedWaveProvider.BufferedDuration.TotalMilliseconds;
                        debugOutput.Text = $"Audio source buffer: {bufferedMs:F0}ms";
                    });
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => debugOutput.Text = $"Audio source error: {ex.Message}");
            }
        }

        private void Microphone_DataAvailable(object? sender, WaveInEventArgs e)
        {
            try
            {
                if (microphoneBuffer != null)
                {
                    if (microphoneBuffer.BufferedBytes > microphoneBuffer.WaveFormat.AverageBytesPerSecond * 2)
                    {
                        microphoneBuffer.ClearBuffer();
                        Dispatcher.Invoke(() => debugOutput.Text = "Microphone buffer cleared - overflow prevented");
                    }
                    microphoneBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
                    
                    Dispatcher.Invoke(() => 
                    {
                        var bufferedMs = microphoneBuffer.BufferedDuration.TotalMilliseconds;
                        debugOutput.Text = $"Microphone buffer: {bufferedMs:F0}ms";
                    });
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => debugOutput.Text = $"Microphone error: {ex.Message}");
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                capture?.StopRecording();
                capture?.Dispose();

                microphoneInput?.StopRecording();
                microphoneInput?.Dispose();

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
                if (capabilities.ProductName.Contains("CABLE", StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return -1;
        }

        private void StopAndCleanup()
        {
            capture?.StopRecording();
            capture?.Dispose();

            microphoneInput?.StopRecording();
            microphoneInput?.Dispose();

            waveOutToSpeakers?.Stop();
            waveOutToSpeakers?.Dispose();

            waveOutToVirtualDevice?.Stop();
            waveOutToVirtualDevice?.Dispose();

            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
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
