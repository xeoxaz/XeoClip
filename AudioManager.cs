using NAudio.Wave;
using System;
using System.IO;

namespace XeoClip
{
	internal class AudioManager
	{
		private WasapiLoopbackCapture? loopbackCapture;
		private WaveFileWriter? waveFileWriter;
		public string? AudioFilePath { get; private set; }

		public void StartRecording(string timestampDir, string sharedTimestamp)
		{
			Console.WriteLine("Starting system audio recording...");
			AudioFilePath = GenerateOutputFileName(timestampDir, "audio", "wav", sharedTimestamp);

			// Initialize WASAPI Loopback Capture
			loopbackCapture = new WasapiLoopbackCapture();

			// Set up the WaveFileWriter to save audio data
			waveFileWriter = new WaveFileWriter(AudioFilePath, loopbackCapture.WaveFormat);

			loopbackCapture.DataAvailable += (s, e) =>
			{
				waveFileWriter?.Write(e.Buffer, 0, e.BytesRecorded);
			};

			loopbackCapture.RecordingStopped += (s, e) =>
			{
				waveFileWriter?.Dispose();
				waveFileWriter = null;
				loopbackCapture?.Dispose();
				loopbackCapture = null;
			};

			loopbackCapture.StartRecording();
		}

		public void StopRecording()
		{
			if (loopbackCapture == null)
			{
				Console.WriteLine("No active audio recording to stop.");
				return;
			}

			try
			{
				Console.WriteLine("Stopping system audio recording...");
				loopbackCapture.StopRecording();
				Console.WriteLine($"Audio saved to: {AudioFilePath}");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error stopping audio recording: {ex.Message}");
			}
		}

		private string GenerateOutputFileName(string directory, string prefix, string extension, string sharedTimestamp)
			=> Path.Combine(directory, $"{prefix}_{sharedTimestamp}.{extension}");
	}
}