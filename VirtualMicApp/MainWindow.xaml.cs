using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Windows;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace VirtualMicApp
{
    public static class AudioDeviceHelper
    {
        public static int FindVBCableDeviceIndex()
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


    public class RelayCommand : ICommand
    {
        private readonly Action execute;
        private readonly Func<bool>? canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            this.execute = execute;
            this.canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => canExecute == null || canExecute();

        public void Execute(object? parameter) => execute();

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    public class SoundEffectSlot : INotifyPropertyChanged
    {
        private string _name = "Empty";
        private bool _canPlay = false;
        private bool _canPause = false;
        private bool _canStop = false;
        private WaveOutEvent? _player;
        private AudioFileReader? _audioReader;

        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                OnPropertyChanged();
            }
        }

        public bool CanPlay
        {
            get => _canPlay;
            set
            {
                _canPlay = value;
                OnPropertyChanged();
                (PlayCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public bool CanPause
        {
            get => _canPause;
            set
            {
                _canPause = value;
                OnPropertyChanged();
                (PauseCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public bool CanStop
        {
            get => _canStop;
            set
            {
                _canStop = value;
                OnPropertyChanged();
                (StopCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public ICommand LoadCommand { get; }
        public ICommand PlayCommand { get; }
        public ICommand PauseCommand { get; }
        public ICommand StopCommand { get; }

        public SoundEffectSlot()
        {
            LoadCommand = new RelayCommand(LoadSound);
            PlayCommand = new RelayCommand(PlaySound, () => CanPlay);
            PauseCommand = new RelayCommand(PauseSound, () => CanPause);
            StopCommand = new RelayCommand(StopSound, () => CanStop);
        }

        private void LoadSound()
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Audio Files|*.wav;*.mp3",
                Title = "Select a Sound Effect"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    _audioReader?.Dispose();
                    _audioReader = new AudioFileReader(openFileDialog.FileName);

                    Name = System.IO.Path.GetFileName(openFileDialog.FileName);
                    _player = new WaveOutEvent();
                    _player.Init(_audioReader);

                    CanPlay = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error loading sound: {ex.Message}");
                }
            }
        }

        private void PlaySound()
        {
            int vbCableDeviceNumber = AudioDeviceHelper.FindVBCableDeviceIndex();
            if (vbCableDeviceNumber == -1)
            {
                MessageBox.Show("VB-Cable not found. Please install or configure VB-Cable.");
                return;
            }

            try
            {
                if (_audioReader == null)
                {
                    MessageBox.Show("Please load a sound file first.");
                    return;
                }

                if (_player != null && _player.PlaybackState == PlaybackState.Paused)
                {
                    _player.Play();
                }
                else
                {
                    _player?.Dispose();
                    _player = new WaveOutEvent
                    {
                        DeviceNumber = vbCableDeviceNumber
                    };

                    _player.Init(_audioReader);
                    _player.Play();
                }

                CanPause = true;
                CanStop = true;
                CanPlay = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error while playing sound: {ex.Message}");
            }
        }

        private void PauseSound()
        {
            if (_player != null && _player.PlaybackState == PlaybackState.Playing)
            {
                _player.Pause();

                CanPause = false;
                CanPlay = true;
            }
        }

        private void StopSound()
        {
            if (_player != null)
            {
                _player.Stop();
                _audioReader.Position = 0;

                CanStop = false;
                CanPause = false;
                CanPlay = true;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    
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
        private List<SoundEffectSlot>? soundEffectSlots;
        public MainWindow()
        {
            InitializeComponent();
            LoadAudioDevices();
            LoadMicrophoneDevices();
            LoadEffects();
            SetupVolumeControls();
            InitializeSoundEffectsSlots();
        }

        private void InitializeSoundEffectsSlots()
        {
            soundEffectSlots = Enumerable.Range(1, 5).Select(_ => new SoundEffectSlot()).ToList();
            soundEffectSlotsControl.ItemsSource = soundEffectSlots;
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
            effectsComboBox.Items.Add("Saturation");
            effectsComboBox.Items.Add("Tremolo");
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
                        case "Saturation":
                            finalOutput = new SaturationEffect(mixer, (float)effectParam1.Value * 10);
                            break;

                        case "Tremolo":
                            finalOutput = new TremoloEffect(mixer, (float)effectParam1.Value * 10);
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
                    Dispatcher.Invoke(() =>
                        debugOutput.Text = $"Started VB-Cable output on device {vbCableDeviceNumber}");
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
                        waveOutToSpeakers = new WaveOutEvent { DeviceNumber = -1 };
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
                MessageBox.Show($"An error occurred while starting: {ex.Message}. Please inform k_z. in discord.");
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

    public class SaturationEffect : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly float _saturationLevel;

        public SaturationEffect(ISampleProvider source, float saturationLevel = 5.0f)
        {
            _source = source;
            _saturationLevel = saturationLevel;
            WaveFormat = source.WaveFormat;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _source.Read(buffer, offset, count);
            for (int i = 0; i < read; i++)
            {
                buffer[i + offset] *= _saturationLevel;
                buffer[i + offset] = Math.Clamp(buffer[i + offset], -1.0f, 1.0f);
            }

            return read;
        }
    }

    public class ReverbEffect : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly float[] _delayBuffer;
        private int _bufferIndex;

        public float ReverbMix { get; set; } = 0.5f;

        public ReverbEffect(ISampleProvider source, float delaySec = 0.4f, float mix = 0.5f)
        {
            _source = source;
            WaveFormat = source.WaveFormat;

            int bufferSize = (int)(source.WaveFormat.SampleRate * delaySec * source.WaveFormat.Channels);
            _delayBuffer = new float[bufferSize];
            ReverbMix = mix;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _source.Read(buffer, offset, count);
            for (int i = 0; i < read; i++)
            {
                int delayBufferIndex = (_bufferIndex + i) % _delayBuffer.Length;

                float delayedSample = _delayBuffer[delayBufferIndex];
                float inputSample = buffer[i + offset];

                buffer[i + offset] = inputSample * (1 - ReverbMix) + delayedSample * ReverbMix;
                _delayBuffer[delayBufferIndex] = inputSample;
            }

            _bufferIndex += read;
            return read;
        }
    }

    public class TremoloEffect : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly float _frequency;
        private int _sampleCounter;

        public TremoloEffect(ISampleProvider source, float frequency = 5.0f)
        {
            _source = source;
            _frequency = frequency;
            WaveFormat = source.WaveFormat;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _source.Read(buffer, offset, count);

            for (int i = 0; i < read; i++)
            {
                double modulation =
                    0.5 * (1.0 + Math.Sin(2 * Math.PI * _frequency * _sampleCounter / WaveFormat.SampleRate));
                buffer[i + offset] *= (float)modulation;
                _sampleCounter++;
            }

            return read;
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