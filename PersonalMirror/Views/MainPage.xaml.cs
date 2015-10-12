using Microsoft.ProjectOxford.Face;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.Sensors;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace PersonalMirror.Views
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private MediaCapture _mediaCapture;
        private bool _isExternalCamera;
        private bool _isInitialized;
        private FaceDetectionEffect _faceDetectionEffect;
        private FaceServiceClient _instance;
        private bool _faceInView;

        public MainPage()
        {
            this.InitializeComponent();
        }

        protected async override void OnNavigatedTo(NavigationEventArgs e)
        {
            _instance = new FaceServiceClient("5fd4bdd5a80c4ab086ab1267b75d0e4c");
            base.OnNavigatedTo(e);
            await InitializeCameraAsync();
            await CreateFaceDetectionEffectAsync();

        }

        private async Task InitializeCameraAsync()
        {
            Debug.WriteLine("InitializeCameraAsync");

            if (_mediaCapture == null)
            {
                // Attempt to get the front camera if one is available, but use any camera device if not
                var cameraDevice = await TryGetFrontCamera();

                if (cameraDevice == null)
                {
                    Debug.WriteLine("No camera device found!");
                    return;
                }

                // Create MediaCapture and its settings
                _mediaCapture = new MediaCapture();

                // Register for a notification when video recording has reached the maximum time and when something goes wrong
                _mediaCapture.Failed += HandleMediaCaptureFailed;

                var settings = new MediaCaptureInitializationSettings { VideoDeviceId = cameraDevice.Id };

                // Initialize MediaCapture
                try
                {
                    await _mediaCapture.InitializeAsync(settings);
                    _isInitialized = true;
                }
                catch (UnauthorizedAccessException)
                {
                    Debug.WriteLine("The app was denied access to the camera");
                }

                // If initialization succeeded, start the preview
                if (_isInitialized)
                {
                    // Figure out where the camera is located
                    if (cameraDevice.EnclosureLocation == null || cameraDevice.EnclosureLocation.Panel == Windows.Devices.Enumeration.Panel.Unknown)
                    {
                        // No information on the location of the camera, assume it's an external camera, not integrated on the device
                        _isExternalCamera = true;
                    }
                    else
                    {
                        // Camera is fixed on the device
                        _isExternalCamera = false;
                    }

                    await StartPreviewAsync();
                }
            }
        }

        private async Task StartPreviewAsync()
        {
            // Prevent the device from sleeping while the preview is running
            //_displayRequest.RequestActive();

            // Set the preview source in the UI and mirror it if necessary
            //PreviewControl.Source = _mediaCapture;
            //PreviewControl.FlowDirection = _mirroringPreview ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

            // Start the preview
            //await _mediaCapture.StartPreviewAsync();
            var stream = new InMemoryRandomAccessStream();
            await _mediaCapture.StartRecordToStreamAsync(MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Auto), stream);
            //_previewProperties = _mediaCapture.VideoDeviceController.GetMediaStreamProperties(MediaStreamType.VideoPreview);

            //// Initialize the preview to the current orientation
            //if (_previewProperties != null)
            //{
            //    _displayOrientation = _displayInformation.CurrentOrientation;

            //    await SetPreviewRotationAsync();
            //}
        }

        private async Task CreateFaceDetectionEffectAsync()
        {
            // Create the definition, which will contain some initialization settings
            var definition = new FaceDetectionEffectDefinition();

            // To ensure preview smoothness, do not delay incoming samples
            definition.SynchronousDetectionEnabled = false;

            // In this scenario, choose detection speed over accuracy
            definition.DetectionMode = FaceDetectionMode.HighPerformance;

            // Add the effect to the preview stream
            _faceDetectionEffect = (FaceDetectionEffect)await _mediaCapture.AddVideoEffectAsync(definition, MediaStreamType.VideoPreview);

            // Register for face detection events
            _faceDetectionEffect.FaceDetected += HandleFaceDetectionEffectFaceDetected;

            // Choose the shortest interval between detection events
            _faceDetectionEffect.DesiredDetectionInterval = TimeSpan.FromMilliseconds(200);

            // Start detecting faces
            _faceDetectionEffect.Enabled = true;
        }

        private async void HandleFaceDetectionEffectFaceDetected(FaceDetectionEffect sender, FaceDetectedEventArgs args)
        {
            if(_faceInView)
            {
                return;
            }

            _faceInView = true;
            await TakePhotoAsync();
            await AnalyzePhotoAsync();

            
        }

        private void HandleMediaCaptureFailed(MediaCapture sender, MediaCaptureFailedEventArgs errorEventArgs)
        {
            throw new NotImplementedException();
        }

        private static async Task<DeviceInformation> TryGetFrontCamera()
        {
            // Get available devices for capturing pictures
            var allVideoDevices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);

            // Get the desired camera by panel
            DeviceInformation desiredDevice = allVideoDevices.FirstOrDefault(x => x.EnclosureLocation != null && x.EnclosureLocation.Panel == Windows.Devices.Enumeration.Panel.Front);

            // If there is no device mounted on the desired panel, return the first device found
            return desiredDevice ?? allVideoDevices.FirstOrDefault();
        }

        private async Task TakePhotoAsync()
        {
            var stream = new InMemoryRandomAccessStream();

            try
            {
                Debug.WriteLine("Taking photo...");
                await _mediaCapture.CapturePhotoToStreamAsync(ImageEncodingProperties.CreateJpeg(), stream);
                Debug.WriteLine("Photo taken!");

                var photoOrientation = PhotoOrientation.Normal;

                await ReencodeAndSavePhotoAsync(stream, photoOrientation);
            }
            catch (Exception ex)
            {
                // File I/O errors are reported as exceptions
                Debug.WriteLine("Exception when taking a photo: {0}", ex.ToString());
            }
        }

        private async Task AnalyzePhotoAsync()
        {
            var stream = new InMemoryRandomAccessStream();

            try
            {
                Debug.WriteLine("Taking photo...");
                await _mediaCapture.CapturePhotoToStreamAsync(ImageEncodingProperties.CreateJpeg(), stream);
                Debug.WriteLine("Photo taken!");

                await DetectPhotoAsync(stream, PhotoOrientation.Normal);
                
            }
            catch (Exception ex)
            {
                // File I/O errors are reported as exceptions
                Debug.WriteLine("Exception when taking a photo: {0}", ex.ToString());
            }
        }

        /// <summary>
        /// Applies the given orientation to a photo stream and saves it as a StorageFile
        /// </summary>
        /// <param name="stream">The photo stream</param>
        /// <param name="photoOrientation">The orientation metadata to apply to the photo</param>
        /// <returns></returns>
        private static async Task ReencodeAndSavePhotoAsync(IRandomAccessStream stream, PhotoOrientation photoOrientation)
        {
            using (var inputStream = stream)
            {
                var decoder = await BitmapDecoder.CreateAsync(inputStream);

                var file = await KnownFolders.PicturesLibrary.CreateFileAsync("SimplePhoto.jpeg", CreationCollisionOption.GenerateUniqueName);

                using (var outputStream = await file.OpenAsync(FileAccessMode.ReadWrite))
                {
                    var encoder = await BitmapEncoder.CreateForTranscodingAsync(outputStream, decoder);

                    var properties = new BitmapPropertySet { { "System.Photo.Orientation", new BitmapTypedValue(photoOrientation, PropertyType.UInt16) } };

                    await encoder.BitmapProperties.SetPropertiesAsync(properties);
                    await encoder.FlushAsync();
                }
            }
        }

        private async Task DetectPhotoAsync(IRandomAccessStream stream, PhotoOrientation photoOrientation)
        {
            using (var inputStream = stream)
            {
                var decoder = await BitmapDecoder.CreateAsync(inputStream);

                var file = await KnownFolders.PicturesLibrary.CreateFileAsync("SimplePhoto.jpeg", CreationCollisionOption.GenerateUniqueName);

                using (var outputStream = new InMemoryRandomAccessStream())
                {
                    var encoder = await BitmapEncoder.CreateForTranscodingAsync(outputStream, decoder);

                    var properties = new BitmapPropertySet { { "System.Photo.Orientation", new BitmapTypedValue(photoOrientation, PropertyType.UInt16) } };

                    await encoder.BitmapProperties.SetPropertiesAsync(properties);
                    await encoder.FlushAsync();

                    var faces = await _instance.DetectAsync(outputStream.AsStream(), false, true, true, false);
                }
            }
        }
    }
}
