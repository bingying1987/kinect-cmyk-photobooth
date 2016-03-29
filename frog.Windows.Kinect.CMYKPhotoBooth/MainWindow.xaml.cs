
namespace frog.Windows.Kinect.CMYKPhotoBooth
{
	using System;
	using System.Collections.Generic;
	using System.ComponentModel;
	using System.Drawing;
	using System.Globalization;
	using System.IO;
	using System.Linq;
	using System.Text;
	using System.Threading;
	using System.Windows;
	using System.Windows.Controls;
	using System.Windows.Input;
	using System.Windows.Media.Animation;
	using System.Windows.Media;
	using System.Windows.Media.Imaging;
	using System.Windows.Threading;

	using Microsoft.Kinect;

	using ImageProcessor;

	public partial class MainWindow : Window
    {
		private const string PHOTO_PUBLISHING_UNC_PATH = @"\\10.118.105.226.Kinect.CMYKPhotoBooth\";
		private const bool IS_PHOTO_PUBLISHING_ENABLED = false;

		private readonly string localPhotosSavePath = System.Environment.CurrentDirectory + @"\Photos";

		private readonly int bytesPerPixel = (PixelFormats.Bgr32.BitsPerPixel + 7) / 8;

        private KinectSensor kinectSensor = null;
        private CoordinateMapper coordinateMapper = null;
        private MultiSourceFrameReader multiFrameSourceReader = null;

        private WriteableBitmap liveBitmap = null;
		private uint bitmapBackBufferSize = 0;
        private DepthSpacePoint[] colorMappedToDepthPoints = null;

		private Body[] bodies = null;
		private bool hasTrackedBodies = false;

		private bool isCapturing = false;
		private int countdownSeconds = 5;
		private int captureIndex = 0;

		private DispatcherTimer countdownTimer = new DispatcherTimer();
		private DispatcherTimer captureTimer = new DispatcherTimer();
		private DispatcherTimer displayTimer = new DispatcherTimer();

		FileSystemWatcher watcher;
		private DispatcherTimer slideshowTimer = new DispatcherTimer();
		private int slideshowIndex = 0;
		private List<string> photos = new List<string>();
		private bool hasNewPhoto = false;

		Random random = new Random();

		private Storyboard flashStoryboard;
		private Storyboard capture0Storyboard;
		private Storyboard capture1Storyboard;
		private Storyboard capture2Storyboard;
		private Storyboard capture3Storyboard;

		public MainWindow()
        {
			this.Initialized += MainWindow_Initialized;
			this.Loaded += MainWindow_Loaded;
			this.Closing += MainWindow_Closing;

			this.KeyDown += MainWindow_KeyDown;

            this.InitializeComponent();
		}

		private void MainWindow_KeyDown(object sender, KeyEventArgs e)
		{
			switch (e.Key)
			{
				case Key.F: // toggle full screen 
					this.WindowState = (this.WindowState == WindowState.Normal) ? WindowState.Maximized : WindowState.Normal;
					break;
				case Key.Escape: // exit application
					App.Current.Shutdown();
					break;
				default:
					break;
			}
		}

		private void MainWindow_Initialized(object sender, EventArgs e)
		{
			this.DataContext = this;

			Directory.CreateDirectory(localPhotosSavePath);

			countdownTimer.Tick += new EventHandler(countdownTimer_Tick);
			countdownTimer.Interval = new TimeSpan(0, 0, 1);

			captureTimer.Tick += new EventHandler(captureTimer_Tick);
			captureTimer.Interval = new TimeSpan(0, 0, 3);

			displayTimer.Tick += new EventHandler(displayTimer_Tick);
			displayTimer.Interval = new TimeSpan(0, 0, 8);

			slideshowTimer.Tick += new EventHandler(slideshowTimer_Tick);
			slideshowTimer.Interval = new TimeSpan(0, 0, 6);

			this.kinectSensor = KinectSensor.GetDefault();

			this.multiFrameSourceReader = this.kinectSensor.OpenMultiSourceFrameReader(FrameSourceTypes.Color | FrameSourceTypes.Depth | FrameSourceTypes.Body | FrameSourceTypes.BodyIndex);
			this.multiFrameSourceReader.MultiSourceFrameArrived += this.Reader_MultiSourceFrameArrived;
			this.coordinateMapper = this.kinectSensor.CoordinateMapper;

			FrameDescription depthFrameDescription = this.kinectSensor.DepthFrameSource.FrameDescription;
			int depthWidth = depthFrameDescription.Width;
			int depthHeight = depthFrameDescription.Height;

			FrameDescription colorFrameDescription = this.kinectSensor.ColorFrameSource.FrameDescription;
			int colorWidth = colorFrameDescription.Width;
			int colorHeight = colorFrameDescription.Height;

			this.colorMappedToDepthPoints = new DepthSpacePoint[colorWidth * colorHeight];

			this.liveBitmap = new WriteableBitmap(colorWidth, colorHeight, 96.0, 96.0, PixelFormats.Bgra32, null);

			this.bitmapBackBufferSize = (uint)((this.liveBitmap.BackBufferStride * (this.liveBitmap.PixelHeight - 1)) + (this.liveBitmap.PixelWidth * this.bytesPerPixel));

			flashStoryboard = this.FindResource("flashStoryboard") as Storyboard;
			capture0Storyboard = this.FindResource("capture0Storyboard") as Storyboard;
			capture1Storyboard = this.FindResource("capture1Storyboard") as Storyboard;
			capture2Storyboard = this.FindResource("capture2Storyboard") as Storyboard;
			capture3Storyboard = this.FindResource("capture3Storyboard") as Storyboard;
		}

		private void MainWindow_Loaded(object sender, RoutedEventArgs e)
		{
			string[] args = Environment.GetCommandLineArgs();
			if (args.Length > 1)
			{
				if (args[1] == "/s")
				{
					LoadPhotos();

					watcher = new FileSystemWatcher();
					watcher.Path = localPhotosSavePath;
					watcher.NotifyFilter = NotifyFilters.LastAccess | NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName;
					watcher.Filter = "*.png";
					watcher.Created += new FileSystemEventHandler(Watcher_OnChanged);
					watcher.EnableRaisingEvents = true;

					countdown.Visibility = Visibility.Collapsed;
					captures.Visibility = Visibility.Collapsed;
				}
			}
			else
			{
				liveImage.Source = this.liveBitmap;
				this.kinectSensor.Open();
			}
		}

		private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (this.multiFrameSourceReader != null)
            {
                this.multiFrameSourceReader.Dispose();
                this.multiFrameSourceReader = null;
            }

            if (this.kinectSensor != null)
            {
                this.kinectSensor.Close();
                this.kinectSensor = null;
            }
        }

        private void Reader_MultiSourceFrameArrived(object sender, MultiSourceFrameArrivedEventArgs e)
        {
			int depthWidth = 0;
            int depthHeight = 0;
                    
            DepthFrame depthFrame = null;
            ColorFrame colorFrame = null;
            BodyIndexFrame bodyIndexFrame = null;
			BodyFrame bodyFrame = null;
            bool isBitmapLocked = false;

			try
			{
				MultiSourceFrame multiSourceFrame = e.FrameReference.AcquireFrame();

				depthFrame = multiSourceFrame.DepthFrameReference.AcquireFrame();
				colorFrame = multiSourceFrame.ColorFrameReference.AcquireFrame();
				bodyIndexFrame = multiSourceFrame.BodyIndexFrameReference.AcquireFrame();
				bodyFrame = multiSourceFrame.BodyFrameReference.AcquireFrame();

				if ((depthFrame == null) || (colorFrame == null) || (bodyIndexFrame == null) || (bodyFrame == null))
				{
					return;
				}

				if (this.bodies == null)
				{
					this.bodies = new Body[bodyFrame.BodyCount];
				}

				bodyFrame.GetAndRefreshBodyData(this.bodies);
				bodyFrame.Dispose();
				bodyFrame = null;

				bool isBodyTracked = false;
				foreach (Body body in this.bodies)
				{
					if (body.IsTracked)
					{
						isBodyTracked = true;
						continue;
					}
				}
				hasTrackedBodies = isBodyTracked;
				if (hasTrackedBodies && !isCapturing)
				{
					BeginCountdown();
				}

				FrameDescription depthFrameDescription = depthFrame.FrameDescription;

				depthWidth = depthFrameDescription.Width;
				depthHeight = depthFrameDescription.Height;

				using (KinectBuffer depthFrameData = depthFrame.LockImageBuffer())
				{
					this.coordinateMapper.MapColorFrameToDepthSpaceUsingIntPtr(
						depthFrameData.UnderlyingBuffer,
						depthFrameData.Size,
						this.colorMappedToDepthPoints);
				}

				depthFrame.Dispose();
				depthFrame = null;

				this.liveBitmap.Lock();
				isBitmapLocked = true;

				colorFrame.CopyConvertedFrameDataToIntPtr(this.liveBitmap.BackBuffer, this.bitmapBackBufferSize, ColorImageFormat.Bgra);

				colorFrame.Dispose();
				colorFrame = null;

				using (KinectBuffer bodyIndexData = bodyIndexFrame.LockImageBuffer())
				{
					unsafe
					{
						byte* bodyIndexDataPointer = (byte*)bodyIndexData.UnderlyingBuffer;

						int colorMappedToDepthPointCount = this.colorMappedToDepthPoints.Length;

						fixed (DepthSpacePoint* colorMappedToDepthPointsPointer = this.colorMappedToDepthPoints)
						{
							uint* bitmapPixelsPointer = (uint*)this.liveBitmap.BackBuffer;

							for (int colorIndex = 0; colorIndex < colorMappedToDepthPointCount; ++colorIndex)
							{
								float colorMappedToDepthX = colorMappedToDepthPointsPointer[colorIndex].X;
								float colorMappedToDepthY = colorMappedToDepthPointsPointer[colorIndex].Y;

								if (!float.IsNegativeInfinity(colorMappedToDepthX) &&
									!float.IsNegativeInfinity(colorMappedToDepthY))
								{
									int depthX = (int)(colorMappedToDepthX + 0.5f);
									int depthY = (int)(colorMappedToDepthY + 0.5f);

									if ((depthX >= 0) && (depthX < depthWidth) && (depthY >= 0) && (depthY < depthHeight))
									{
										int depthIndex = (depthY * depthWidth) + depthX;
										if (bodyIndexDataPointer[depthIndex] != 0xff)
										{
											continue;
										}
									}
								}

								bitmapPixelsPointer[colorIndex] = 0;
							}
						}

						this.liveBitmap.AddDirtyRect(new Int32Rect(0, 0, this.liveBitmap.PixelWidth, this.liveBitmap.PixelHeight));
					}
				}
			}
			finally
			{
				if (isBitmapLocked) { this.liveBitmap.Unlock(); }
				if (depthFrame != null) { depthFrame.Dispose(); }
				if (colorFrame != null) { colorFrame.Dispose(); }
				if (bodyIndexFrame != null) { bodyIndexFrame.Dispose(); }
				if (bodyFrame != null) { bodyFrame.Dispose();}
			}
        }

		private void BeginCountdown()
		{
			isCapturing = true;
			countdownSeconds = 5;
			SetCountdown();
			countdownTimer.Start();
			capture0Storyboard.Begin(this);
		}

		private void countdownTimer_Tick(object sender, EventArgs e)
		{
			countdownSeconds--;

			SetCountdown();

			if (countdownSeconds < 1)
			{
				countdownTimer.Stop();
				BeginCaptures();
			}
		}

		private void SetCountdown()
		{
			countdownOpacityEllipse.StrokeDashArray = new DoubleCollection { 3.1416 - (3.1416 - (3.1416 * (countdownSeconds / 5d))), countdownOpacityEllipse.StrokeDashArray[1] };
			countdown.Opacity = countdownSeconds > 0 ? 1 : 0;
		}

		private void BeginCaptures()
		{
			if (hasTrackedBodies)
			{
				captureIndex = 0;
				CaptureLayer();
				captureTimer.Start();
			}
			else
			{
				isCapturing = false;
			}
		}

		private void captureTimer_Tick(object sender, EventArgs e)
		{
			captureTimer.Stop();
			captureIndex++;

			if (captureIndex < 4)
			{
				CaptureLayer();
				captureTimer.Start();
				if (captureIndex == 3)
				{
					CompositeLayers.Background.Opacity = 1;
				}
			}
			else
			{
				EndCaptures();
			}
		}

		private void CaptureLayer()
		{
			SetFlashFill(captureIndex);
			flashStoryboard.Begin(this);

			switch (captureIndex)
			{
				case 0:
					capture1Storyboard.Begin(this);
					break;
				case 1:
					capture2Storyboard.Begin(this);
					break;
				case 2:
					capture3Storyboard.Begin(this);
					break;
				default:
					break;
			}

			RenderTargetBitmap renderBitmap = new RenderTargetBitmap((int)CompositeImage.ActualWidth, (int)CompositeImage.ActualHeight, 96.0, 96.0, PixelFormats.Pbgra32);
			DrawingVisual dv = new DrawingVisual();
			using (DrawingContext dc = dv.RenderOpen())
			{
				VisualBrush brush = new VisualBrush(CompositeImage);
				dc.DrawRectangle(brush, null, new Rect(new System.Windows.Point(), new System.Windows.Size(CompositeImage.ActualWidth, CompositeImage.ActualHeight)));
			}
			renderBitmap.Render(dv);

			byte[] encoded;

			using (MemoryStream stream = new MemoryStream())
			{
				var encoder = new PngBitmapEncoder();
				encoder.Frames.Add(BitmapFrame.Create(renderBitmap));
				encoder.Save(stream);
				encoded = stream.ToArray();
			}

			BitmapImage processed = new BitmapImage();
			double opacity = 1;

			using (MemoryStream stream = new MemoryStream())
			{
				using (ImageProcessor.ImageFactory imageFactory = new ImageProcessor.ImageFactory(false))
				{
					imageFactory.Load(encoded);
					imageFactory.Filter(ImageProcessor.Imaging.Filters.Photo.MatrixFilters.BlackWhite);
					switch (captureIndex)
					{
						case 0:
							break;
						case 1:
							imageFactory.Tint(System.Drawing.Color.Magenta);
							opacity = .4;
							break;
						case 2:
							imageFactory.Tint(System.Drawing.Color.Cyan);
							opacity = .3;
							break;
						case 3:
							imageFactory.Tint(System.Drawing.Color.Yellow);
							opacity = .2;
							break;
						default:
							break;
					}
					imageFactory.Save(stream);
				}

				processed.BeginInit();
				processed.CacheOption = BitmapCacheOption.OnLoad;
				processed.StreamSource = stream;
				processed.EndInit();
			}

			System.Windows.Controls.Image image = new System.Windows.Controls.Image()
			{
				Source = processed,
				Stretch = Stretch.None,
				Opacity = opacity
			};
			CompositeLayers.Children.Add(image);
		}

		private void EndCaptures()
		{
			if (hasTrackedBodies)
			{
				RenderTargetBitmap renderBitmap = new RenderTargetBitmap((int)CompositeLayers.ActualWidth, (int)CompositeLayers.ActualHeight, 96.0, 96.0, PixelFormats.Pbgra32);
				DrawingVisual dv = new DrawingVisual();
				using (DrawingContext dc = dv.RenderOpen())
				{
					VisualBrush brush = new VisualBrush(CompositeLayers);
					dc.DrawRectangle(brush, null, new Rect(new System.Windows.Point(), new System.Windows.Size(CompositeLayers.ActualWidth, CompositeLayers.ActualHeight)));
				}
				renderBitmap.Render(dv);

				BitmapEncoder encoder = new PngBitmapEncoder();
				encoder.Frames.Add(BitmapFrame.Create(renderBitmap));   

				string id = Int32.Parse(DateTime.Now.ToString("HHmmss")).ToString("X2");
				string path = localPhotosSavePath + @"\" + id + ".png";

				try
				{
					using (FileStream fs = new FileStream(path, FileMode.Create))
					{
						encoder.Save(fs);
					}
				}
				catch (IOException) { }

				photoId.Text = id;
				link.Opacity = 1;

				displayTimer.Start();

				if (IS_PHOTO_PUBLISHING_ENABLED)
				{
					new Thread(() =>
					{
						try
						{
							File.Copy(path, PHOTO_PUBLISHING_UNC_PATH + id + ".png");
						}
						catch { }
					}).Start();
				}
			}
			else
			{
				ResetLayers();
			}
		}

		private void displayTimer_Tick(object sender, EventArgs e)
		{
			displayTimer.Stop();
			ResetLayers();
		}

		private void ResetLayers()
		{
			link.Opacity = 0;
			CompositeLayers.Children.Clear();
			CompositeLayers.Background.Opacity = 0;
			isCapturing = false;

			countdownSeconds = 5;
			SetCountdown();
		}

		private void slideshowTimer_Tick(object sender, EventArgs e)
		{
			slideshowTimer.Stop();
			if (hasNewPhoto)
			{
				LoadPhotos();
				hasNewPhoto = false;
			}
			else
			{
				slideshowIndex++;
				if (slideshowIndex >= photos.Count)
				{
					slideshowIndex = 0;
				}
				ShowPhoto();
				slideshowTimer.Start();
			}
		}

		private void Watcher_OnChanged(object source, FileSystemEventArgs e)
		{
			hasNewPhoto = true;
		}

		private void ShowPhoto()
		{
			try
			{
				SetFlashFill(random.Next(0, 4));
				flashStoryboard.Begin(this);

				Uri photoUri = new Uri(photos[slideshowIndex]);
				liveImage.Source = new BitmapImage(photoUri);
				photoId.Text = Path.GetFileNameWithoutExtension(photoUri.LocalPath);
				link.Opacity = 1;
			}
			catch { }
		}

		private void LoadPhotos()
		{
			try {
				slideshowTimer.Stop();
				photos.Clear();

				DirectoryInfo di = new DirectoryInfo(localPhotosSavePath);
				FileSystemInfo[] files = di.GetFileSystemInfos("*.png");
				var orderedFiles = files.OrderByDescending(f => f.CreationTime);
				foreach (FileSystemInfo fsi in orderedFiles)
				{
					photos.Add(fsi.FullName);
				}

				if (photos.Count > 0)
				{
					slideshowIndex = 0;
					ShowPhoto();
					slideshowTimer.Start();
				}
			} catch { }
		}

		private void SetFlashFill(int index)
		{
			try
			{
				switch (index)
				{
					case 0:
						flash.Fill = System.Windows.Media.Brushes.White;
						break;
					case 1:
						flash.Fill = System.Windows.Media.Brushes.Magenta;
						break;
					case 2:
						flash.Fill = System.Windows.Media.Brushes.Cyan;
						break;
					case 3:
						flash.Fill = System.Windows.Media.Brushes.Yellow;
						break;
					default:
						break;
				}
			}
			catch { }
		}
	}
}
