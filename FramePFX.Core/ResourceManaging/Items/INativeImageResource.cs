namespace FramePFX.Core.ResourceManaging {
    public interface INativeImageResource : INativeResource {
        /// <summary>
        /// Called when the image for this resource changed
        /// </summary>
        /// <param name="filePath"></param>
        void OnImageChanged(string filePath);
    }
}