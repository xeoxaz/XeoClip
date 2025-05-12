using OpenCvSharp;
using System;
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
		private readonly string timestampDir;

		public IconWatcher(string baseDirectory, FFmpegManager ffmpegManager)
		{
			// Ensure icons folder exists
			iconsPath = Path.Combine(baseDirectory, "icons");
			this.ffmpegManager = ffmpegManager;
			this.timestampDir = baseDirectory;

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

			// Load icons and check if any are present
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
					using var capture = new VideoCapture(0); // Replace "0" with video feed path if needed
					if (!capture.IsOpened())
					{
						Console.WriteLine("Failed to open video feed. IconWatcher will stop.");
						return;
					}

					while (isWatching)
					{
						try
						{
							if (TryDetectIcons(capture, icons, out double timestamp))
							{
								Console.WriteLine($"Icon detected at {timestamp:F2} seconds. Clipping video...");
								ffmpegManager.ClipVideo(timestamp - 5, timestamp + 5, timestampDir);
							}
						}
						catch (Exception ex)
						{
							Console.WriteLine($"Error during icon detection: {ex.Message}");
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
			{
				IsBackground = true
			};

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
			if (files.Length == 0)
			{
				Console.WriteLine("No icons found in the icons folder.");
			}
			else
			{
				Console.WriteLine($"Loaded {files.Length} icon(s) from {iconsPath}.");
			}

			return Array.ConvertAll(files, file => Cv2.ImRead(file, ImreadModes.Grayscale));
		}

		private bool TryDetectIcons(VideoCapture capture, Mat[] icons, out double timestamp)
		{
			timestamp = 0;

			using var frame = new Mat();
			if (!capture.Read(frame))
			{
				Console.WriteLine("Failed to read frame from video feed.");
				return false;
			}

			foreach (var icon in icons)
			{
				if (DetectIcon(frame, icon))
				{
					timestamp = capture.PosMsec / 1000.0; // Current video timestamp in seconds
					return true;
				}
			}

			return false;
		}

		private bool DetectIcon(Mat frame, Mat icon)
		{
			try
			{
				using var grayFrame = ConvertToGrayscale(frame);
				using var grayIcon = ConvertToGrayscale(icon);

				using var frameEdges = ApplyCannyEdgeDetection(grayFrame);
				using var iconEdges = ApplyCannyEdgeDetection(grayIcon);

				return PerformTemplateMatching(frameEdges, iconEdges, 0.8);
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

		private bool PerformTemplateMatching(Mat frameEdges, Mat iconEdges, double threshold)
		{
			using var result = new Mat();
			Cv2.MatchTemplate(frameEdges, iconEdges, result, TemplateMatchModes.CCoeffNormed);
			Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out _);
			return maxVal > threshold;
		}
	}
}