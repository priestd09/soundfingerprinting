namespace SoundFingerprinting
{
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    using SoundFingerprinting.Audio;
    using SoundFingerprinting.Configuration;
    using SoundFingerprinting.Data;
    using SoundFingerprinting.FFT;
    using SoundFingerprinting.Infrastructure;
    using SoundFingerprinting.Utils;
    using SoundFingerprinting.Wavelets;

    internal class FingerprintService : IFingerprintService
    {
        private readonly ISpectrumService spectrumService;
        private readonly IWaveletDecomposition waveletDecomposition;
        private readonly IFingerprintDescriptor fingerprintDescriptor;
        private readonly IAudioSamplesNormalizer audioSamplesNormalizer;

        public FingerprintService()
            : this(
                DependencyResolver.Current.Get<ISpectrumService>(),
                DependencyResolver.Current.Get<IWaveletDecomposition>(),
                DependencyResolver.Current.Get<IFingerprintDescriptor>(),
                DependencyResolver.Current.Get<IAudioSamplesNormalizer>())
        {
        }

        internal FingerprintService(
            ISpectrumService spectrumService,
            IWaveletDecomposition waveletDecomposition,
            IFingerprintDescriptor fingerprintDescriptor,
            IAudioSamplesNormalizer audioSamplesNormalizer)
        {
            this.spectrumService = spectrumService;
            this.waveletDecomposition = waveletDecomposition;
            this.fingerprintDescriptor = fingerprintDescriptor;
            this.audioSamplesNormalizer = audioSamplesNormalizer;
        }

        public List<Fingerprint> CreateFingerprints(AudioSamples samples, FingerprintConfiguration configuration)
        { 
            NormalizeAudioIfNecessary(samples, configuration);
            var spectrum = spectrumService.CreateLogSpectrogram(samples, configuration.SpectrogramConfig);
            return CreateFingerprintsFromLogSpectrum(spectrum, configuration);
        }

        private List<Fingerprint> CreateFingerprintsFromLogSpectrum(List<SpectralImage> spectralImages, FingerprintConfiguration configuration)
        {
            var fingerprints = new ConcurrentBag<Fingerprint>();
            var till = configuration.SpectrogramConfig.ImageLength * configuration.SpectrogramConfig.LogBins;

            Parallel.ForEach(spectralImages, () => new ushort[till],
                (spectralImage, loop, cachedIndexes) =>
                {
                    waveletDecomposition.DecomposeImageInPlace(spectralImage.Image, spectralImage.Rows, spectralImage.Cols);
                    RangeUtils.PopulateIndexes(till, cachedIndexes);
                    var image = fingerprintDescriptor.ExtractTopWavelets(spectralImage.Image, configuration.TopWavelets, cachedIndexes);
                    if (!image.IsSilence())
                    {
                        fingerprints.Add(new Fingerprint(image, spectralImage.StartsAt, spectralImage.SequenceNumber));
                    }

                    return cachedIndexes;
                }, 
                cachedIndexes => { });

            return fingerprints.ToList();
        }

        private void NormalizeAudioIfNecessary(AudioSamples samples, FingerprintConfiguration configuration)
        {
            if (configuration.NormalizeSignal)
            {
                audioSamplesNormalizer.NormalizeInPlace(samples.Samples);
            }
        }
    }
}
