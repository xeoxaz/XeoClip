using OpenCvSharp;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;

namespace XeoClip
{
	internal class IconWatcher
	{
		private readonly string iconsPath;
		private readonly FFmpegManager ffmpegManager;
		private Thread? watcherThread;
		private volatile bool isWatching;
		private readonly string baseDirectory;

		public IconWatcher(string baseDirectory, FFmpegManager ffmpegManager)
		{
			this.baseDirectory = baseDirectory;
			this.ffmpegManager = ffmpegManager;

			iconsPath = Path.Combine(baseDirectory, "icons");

			if (!Directory.Exists(iconsPath))
			{
				Directory.CreateDirectory(iconsPath);
				Console.WriteLine($"Icons directory created: {iconsPath}");
			}
		}

		public void StartWatching()
		{
			if (isWatching)
			{
				Console.WriteLine("IconWatcher is already running.");
				return;
			}

			var icons = LoadIcons();
			if (icons.Length == 0)
			{
				Console.WriteLine("No icons found in the icons folder. IconWatcher will not run.");
				return;
			}

			isWatching = true;
			Console.WriteLine("Starting IconWatcher...");
			watcherThread = new Thread(() =>
			{
				try
				{
					while (isWatching)
					{
						using var screenshot = CaptureScreen();
						if (TryDetectIcons(screenshot, icons))
						{
							Console.WriteLine("Icon detected! Clipping video...");
							ffmpegManager.ClipVideo(5, 5, baseDirectory); // Adjust as per your directory structure
						}
						Thread.Sleep(100); // Reduce CPU usage
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Error in IconWatcher thread: {ex.Message}");
				}
				finally
				{
					Console.WriteLine("IconWatcher stopped.");
				}
			})
			{ IsBackground = true };

			watcherThread.Start();
		}

		public void StopWatching()
		{
			if (!isWatching)
			{
				Console.WriteLine("IconWatcher is not running.");
				return;
			}

			Console.WriteLine("Stopping IconWatcher...");
			isWatching = false;
			watcherThread?.Join();
			Console.WriteLine("IconWatcher has stopped.");
		}

		private Mat[] LoadIcons()
		{
			var files = Directory.GetFiles(iconsPath, "*.png");
			Console.WriteLine($"Found {files.Length} icon(s) in {iconsPath}.");
			return Array.ConvertAll(files, file => Cv2.ImRead(file, ImreadModes.Grayscale));
		}

		private Bitmap CaptureScreen()
		{
			var bounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
			var screenshot = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);

			using (var graphics = Graphics.FromImage(screenshot))
			{
				graphics.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
			}

			return screenshot;
		}

		private bool TryDetectIcons(Bitmap screenshot, Mat[] icons)
		{
			using var frame = OpenCvSharp.Extensions.BitmapConverter.ToMat(screenshot);
			foreach (var icon in icons)
			{
				if (DetectIcon(frame, icon, out double matchValue))
				{
					Console.WriteLine($"Match Value: {matchValue:F2} (Threshold: 0.8)");
					return true;
				}
			}

			return false;
		}

		private bool DetectIcon(Mat frame, Mat icon, out double matchValue)
		{
			matchValue = 0.0;

			try
			{
				using var grayFrame = ConvertToGrayscale(frame);
				using var frameEdges = ApplyCannyEdgeDetection(grayFrame);

				using var iconEdges = ApplyCannyEdgeDetection(icon);
				return PerformTemplateMatching(frameEdges, iconEdges, 0.8, out matchValue);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error during icon detection: {ex.Message}");
				return false;
			}
		}

		private Mat ConvertToGrayscale(Mat input)
		{
			if (input.Channels() == 1) return input.Clone();

			var grayscale = new Mat();
			Cv2.CvtColor(input, grayscale, ColorConversionCodes.BGR2GRAY);
			return grayscale;
		}

		private Mat ApplyCannyEdgeDetection(Mat input, double threshold1 = 100, double threshold2 = 200)
		{
			var edges = new Mat();
			Cv2.Canny(input, edges, threshold1, threshold2);
			return edges;
		}

		private bool PerformTemplateMatching(Mat frameEdges, Mat iconEdges, double threshold, out double matchValue)
		{
			using var result = new Mat();
			Cv2.MatchTemplate(frameEdges, iconEdges, result, TemplateMatchModes.CCoeffNormed);
			Cv2.MinMaxLoc(result, out _, out matchValue, out _, out _);
			return matchValue > threshold;
		}
	}
}