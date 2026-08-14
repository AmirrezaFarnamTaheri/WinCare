using System;
using System.IO;

namespace WinCare.Application.Diagnostics
{
    public interface IModelManager
    {
        string ModelDirectory { get; }
        string DefaultModelPath { get; }
        bool IsModelAvailable { get; }
        bool EnsureModelDirectoryCreated();
    }

    public sealed class ModelManager : IModelManager
    {
        public string ModelDirectory { get; }
        public string DefaultModelPath => Path.Combine(ModelDirectory, "doctor.onnx");

        public bool IsModelAvailable => File.Exists(DefaultModelPath);

        public ModelManager(string? customBasePath = null)
        {
            if (!string.IsNullOrWhiteSpace(customBasePath))
            {
                ModelDirectory = customBasePath;
            }
            else
            {
                var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                ModelDirectory = Path.Combine(programData, "WinCare", "Models");
            }
        }

        public bool EnsureModelDirectoryCreated()
        {
            try
            {
                if (!Directory.Exists(ModelDirectory))
                {
                    Directory.CreateDirectory(ModelDirectory);
                }
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ModelManager] Failed to create model directory: {ex.Message}");
                return false;
            }
        }
    }
}
