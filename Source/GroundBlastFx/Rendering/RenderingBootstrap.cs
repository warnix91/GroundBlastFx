using GroundBlastFx.Contracts;
using UnityEngine;

namespace GroundBlastFx.Rendering
{
    /// <summary>Enregistre le renderer avant le chargement des scènes de vol.</summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public sealed class RenderingBootstrap : MonoBehaviour
    {
        private void Awake()
        {
            RendererLocator.Factory = () => new VolumetricRenderer();
            DontDestroyOnLoad(this);
        }
    }
}
