using System;
using System.Linq;
using System.Threading;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Thumbly
{
    /// <summary>
    /// The only place that touches ONNX Runtime types. Keeps one session per model file and releases it after two
    /// idle minutes, since a loaded BiRefNet-lite session holds several hundred MB.
    /// </summary>
    internal static class OnnxModel
    {
        private static readonly TimeSpan IdleUnload = TimeSpan.FromMinutes(2);
        private static readonly object Gate = new object();
        private static InferenceSession _session;
        private static string _sessionPath;
        private static Timer _idle;

        public static float[] Run(string modelPath, float[] input)
        {
            lock (Gate)
            {
                var session = Session(modelPath);
                var name = session.InputMetadata.Keys.First();
                var tensor = new DenseTensor<float>(input, new[] { 1, 3, AiMatting.Size, AiMatting.Size });
                using (var results = session.Run(new[] { NamedOnnxValue.CreateFromTensor(name, tensor) }))
                {
                    // BiRefNet's first output is the finest prediction.
                    var output = results.First().AsTensor<float>().ToArray();
                    if (output.Length != AiMatting.Size * AiMatting.Size)
                        throw new InvalidOperationException("The model returned " + output.Length + " values; expected a 1024 × 1024 mask. Is this the " + AiMatting.ModelName + " model?");
                    ScheduleUnload();
                    return output;
                }
            }
        }

        public static string Check(string modelPath)
        {
            lock (Gate)
            {
                var session = Session(modelPath);
                var input = session.InputMetadata.First().Value;
                var output = session.OutputMetadata.First().Value;
                ScheduleUnload();
                if (input.ElementType != typeof(float)) return "The model expects " + input.ElementType.Name + " input; use the full-precision model.onnx, not model_fp16.onnx.";
                if (!input.Dimensions.SequenceEqual(new[] { 1, 3, AiMatting.Size, AiMatting.Size }))
                    return "The model's input is [" + string.Join(", ", input.Dimensions) + "]; " + AiMatting.ModelName + " takes [1, 3, 1024, 1024].";
                if (output.Dimensions.Length != 4 || output.Dimensions[2] != AiMatting.Size || output.Dimensions[3] != AiMatting.Size)
                    return "The model's output is [" + string.Join(", ", output.Dimensions) + "]; expected a 1024 × 1024 mask.";
                return null;
            }
        }

        private static InferenceSession Session(string modelPath)
        {
            if (_session != null && _sessionPath == modelPath) return _session;
            _session?.Dispose();
            _session = null;
            using (var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL })
                _session = new InferenceSession(modelPath, options);
            _sessionPath = modelPath;
            return _session;
        }

        private static void ScheduleUnload()
        {
            if (_idle == null) _idle = new Timer(_ => Unload(), null, IdleUnload, Timeout.InfiniteTimeSpan);
            else _idle.Change(IdleUnload, Timeout.InfiniteTimeSpan);
        }

        private static void Unload()
        {
            lock (Gate)
            {
                _session?.Dispose();
                _session = null;
                _sessionPath = null;
            }
        }
    }
}
