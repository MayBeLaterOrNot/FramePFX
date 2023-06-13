using System.Threading.Tasks;
using FramePFX.Core.Editor.ResourceManaging.ViewModels;

namespace FramePFX.Core.Editor {
    public interface IResourceItemDropHandler {
        Task OnResourceDropped(ResourceItemViewModel resource, long frameBegin);
    }
}