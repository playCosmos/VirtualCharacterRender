using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UniGLTF;
using UniVRM10;
using UnityEngine;
using VCR.Runtime.Tracking;
using VCR.Runtime.Materials.Unity;
using VCR.Runtime.Appearance.Unity;

namespace VCR.Runtime.Character
{
    /// <summary>
    /// One-character VRM runtime loader.
    ///
    /// VRM 0.x is migrated by UniVRM into the same VRM10 runtime path.
    /// A failed/cancelled replacement never removes the currently active model.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Vrm10CharacterLoader : MonoBehaviour
    {
        private const long MaxVrmFileBytes =
            1024L * 1024L * 1024L;

        [Header("Placement")]
        [SerializeField] private Transform characterParent;
        [SerializeField] private Vector3 localPosition = Vector3.zero;
        [SerializeField] private Vector3 localEulerAngles = Vector3.zero;

        [Header("Tracking")]
        [SerializeField] private MonoBehaviour trackingProviderBehaviour;
        [SerializeField] private bool attachTrackingTargets = true;
        [SerializeField] private bool attachMotionSnapshotProvider = true;

        [Header("Materials")]
        [SerializeField] private bool attachMaterialOverrideController = true;
        [SerializeField] private Shader vrm10BuiltInMToonShader;
        [SerializeField] private Shader builtInStandardShader;
        [SerializeField] private Shader vrm10UrpMToonShader;
        [SerializeField] private Shader urpLitShader;
        [SerializeField] private Shader uniUnlitShader;

        [Header("Appearance")]
        [SerializeField] private bool attachAppearanceRuntime = true;

        private CancellationTokenSource _loadCancellation;
        private int _loadGeneration;

        public Vrm10Instance Current { get; private set; }
        public string CurrentPath { get; private set; }

        public event Action<Vrm10Instance> CharacterLoaded;
        public event Action CharacterUnloaded;
        public event Action<Exception> LoadFailed;

        public async Task<Vrm10Instance> LoadAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException(
                    "VRM path is required.",
                    nameof(path));
            }

            path = Path.GetFullPath(path);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "VRM file was not found.",
                    path);
            }

            var fileLength =
                new FileInfo(
                    path)
                    .Length;

            if (fileLength <= 0)
            {
                throw new InvalidDataException(
                    "VRM file is empty.");
            }

            if (fileLength >
                MaxVrmFileBytes)
            {
                throw new InvalidDataException(
                    $"VRM file exceeds the {MaxVrmFileBytes} byte safety limit.");
            }

            CancelPendingLoad();

            var generation = ++_loadGeneration;
            var loadCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
            _loadCancellation = loadCancellation;

            Vrm10Instance loaded = null;

            try
            {
                var materialGenerator =
                    CreateMaterialDescriptorGenerator();

                loaded = await Vrm10.LoadPathAsync(
                    path,
                    canLoadVrm0X: true,
                    controlRigGenerationOption:
                        ControlRigGenerationOption.Generate,
                    showMeshes: true,
                    materialGenerator:
                        materialGenerator,
                    ct: loadCancellation.Token);

                if (loaded == null)
                {
                    throw new InvalidOperationException(
                        "UniVRM returned no instance.");
                }

                if (loadCancellation.IsCancellationRequested ||
                    generation != _loadGeneration)
                {
                    DestroyLoaded(loaded);
                    return null;
                }

                ConfigureLoadedCharacter(loaded);

                var previous = Current;
                Current = loaded;
                CurrentPath = path;

                if (previous != null &&
                    previous != Current)
                {
                    previous.gameObject.SetActive(false);
                    DestroyLoaded(previous);
                }

                NotifyCharacterLoaded(
                    Current);
                return Current;
            }
            catch (OperationCanceledException)
            {
                if (loaded != null &&
                    loaded != Current)
                {
                    DestroyLoaded(loaded);
                }

                return null;
            }
            catch (Exception exception)
            {
                if (loaded != null &&
                    loaded != Current)
                {
                    DestroyLoaded(loaded);
                }

                NotifyLoadFailed(
                    exception);
                throw;
            }
            finally
            {
                if (ReferenceEquals(
                    _loadCancellation,
                    loadCancellation))
                {
                    _loadCancellation = null;
                }

                loadCancellation.Dispose();
            }
        }

        public void Unload()
        {
            CancelPendingLoad();
            _loadGeneration++;

            if (Current == null)
            {
                CurrentPath = null;
                return;
            }

            var current = Current;
            Current = null;
            CurrentPath = null;

            current.gameObject.SetActive(false);
            DestroyLoaded(current);
            NotifyCharacterUnloaded();
        }

        public void CancelPendingLoad()
        {
            if (_loadCancellation == null)
            {
                return;
            }

            var cancellation = _loadCancellation;
            _loadCancellation = null;
            cancellation.Cancel();
        }

        public void ConfigureRuntimeImportShaders(
            Shader builtInMToonShader,
            Shader standardShader,
            Shader urpMToonShader,
            Shader litShader,
            Shader unlitShader)
        {
            vrm10BuiltInMToonShader =
                builtInMToonShader;
            builtInStandardShader =
                standardShader;
            vrm10UrpMToonShader =
                urpMToonShader;
            urpLitShader =
                litShader;
            uniUnlitShader =
                unlitShader;
        }

        public void SetTrackingProvider(
            ITrackingFrameProvider provider)
        {
            var liveProvider =
                IsServiceAlive(provider)
                    ? provider
                    : null;

            trackingProviderBehaviour =
                liveProvider as MonoBehaviour;

            if (Current != null)
            {
                BindTracking(
                    Current,
                    liveProvider);
            }
        }

        private IMaterialDescriptorGenerator
            CreateMaterialDescriptorGenerator()
        {
            var generator =
                Vrm10MaterialDescriptorGeneratorUtility
                    .GetValidVrm10MaterialDescriptorGenerator();

            if (generator is
                UrpVrm10MaterialDescriptorGenerator urp)
            {
                if (vrm10UrpMToonShader != null)
                {
                    urp.MToonMaterialImporter.Shader =
                        vrm10UrpMToonShader;
                }

                if (urpLitShader != null)
                {
                    urp.PbrMaterialImporter.Shader =
                        urpLitShader;
                    urp.DefaultMaterialImporter.Shader =
                        urpLitShader;
                }

                if (uniUnlitShader != null)
                {
                    urp.UnlitMaterialImporter.Shader =
                        uniUnlitShader;
                }

                EnsureUrpImportShaders(
                    urp);
                return urp;
            }

            if (generator is
                BuiltInVrm10MaterialDescriptorGenerator builtIn)
            {
                if (vrm10BuiltInMToonShader != null)
                {
                    builtIn.MToonMaterialImporter.Shader =
                        vrm10BuiltInMToonShader;
                }

                if (builtInStandardShader != null)
                {
                    builtIn.PbrMaterialImporter.Shader =
                        builtInStandardShader;
                    builtIn.DefaultMaterialImporter.Shader =
                        builtInStandardShader;
                }

                if (uniUnlitShader != null)
                {
                    builtIn.UnlitMaterialImporter.Shader =
                        uniUnlitShader;
                }

                EnsureBuiltInImportShaders(
                    builtIn);
                return builtIn;
            }

            return generator;
        }

        private static void EnsureUrpImportShaders(
            UrpVrm10MaterialDescriptorGenerator urp)
        {
            var missing =
                new System.Collections.Generic.List<string>();

            if (urp.MToonMaterialImporter.Shader == null)
            {
                missing.Add(
                    "VRM10/Universal Render Pipeline/MToon10");
            }

            if (urp.PbrMaterialImporter.Shader == null ||
                urp.DefaultMaterialImporter.Shader == null)
            {
                missing.Add(
                    "Universal Render Pipeline/Lit");
            }

            if (urp.UnlitMaterialImporter.Shader == null)
            {
                missing.Add(
                    "UniGLTF/UniUnlit");
            }

            ThrowIfImportShadersMissing(
                "URP",
                missing);
        }

        private static void EnsureBuiltInImportShaders(
            BuiltInVrm10MaterialDescriptorGenerator builtIn)
        {
            var missing =
                new System.Collections.Generic.List<string>();

            if (builtIn.MToonMaterialImporter.Shader == null)
            {
                missing.Add(
                    "VRM10/MToon10");
            }

            if (builtIn.PbrMaterialImporter.Shader == null ||
                builtIn.DefaultMaterialImporter.Shader == null)
            {
                missing.Add(
                    "Standard");
            }

            if (builtIn.UnlitMaterialImporter.Shader == null)
            {
                missing.Add(
                    "UniGLTF/UniUnlit");
            }

            ThrowIfImportShadersMissing(
                "Built-in RP",
                missing);
        }

        private static void ThrowIfImportShadersMissing(
            string pipeline,
            System.Collections.Generic.List<string> missing)
        {
            if (missing.Count == 0)
            {
                return;
            }

            throw new InvalidOperationException(
                "VRM runtime shader dependency is missing " +
                $"for {pipeline}: " +
                string.Join(", ", missing) +
                ". Rebuild the runtime scene/player so the UniVRM " +
                "shader assets are serialized into the build.");
        }

        private void ConfigureLoadedCharacter(
            Vrm10Instance instance)
        {
            var parent =
                characterParent != null
                    ? characterParent
                    : transform;

            instance.transform.SetParent(
                parent,
                worldPositionStays: false);

            instance.transform.localPosition =
                localPosition;
            instance.transform.localRotation =
                Quaternion.Euler(localEulerAngles);

            if (attachMaterialOverrideController)
            {
                var materials =
                    instance.GetComponent<MaterialOverrideController>() ??
                    instance.gameObject.AddComponent<
                        MaterialOverrideController>();

                materials.RefreshSlots();
            }

            if (attachAppearanceRuntime &&
                instance.GetComponent<
                    BasicCharacterAppearanceRuntime>() ==
                null)
            {
                instance.gameObject.AddComponent<
                    BasicCharacterAppearanceRuntime>();
            }

            if (!attachTrackingTargets)
            {
                return;
            }

            var provider =
                trackingProviderBehaviour != null
                    ? trackingProviderBehaviour as
                        ITrackingFrameProvider
                    : null;

            BindTracking(
                instance,
                provider);
        }

        private void BindTracking(
            Vrm10Instance instance,
            ITrackingFrameProvider provider)
        {
            var faceTarget =
                instance.GetComponent<Vrm10TrackingTarget>() ??
                instance.gameObject.AddComponent<
                    Vrm10TrackingTarget>();

            var bodyTarget =
                instance.GetComponent<
                    Vrm10HumanoidPoseTarget>() ??
                instance.gameObject.AddComponent<
                    Vrm10HumanoidPoseTarget>();

            var liveProvider =
                IsServiceAlive(provider)
                    ? provider
                    : null;

            faceTarget.SetTrackingProvider(
                liveProvider);
            bodyTarget.SetTrackingProvider(
                liveProvider);

            if (attachMotionSnapshotProvider &&
                instance.GetComponent<
                    Vrm10MotionSnapshotProvider>() == null)
            {
                instance.gameObject.AddComponent<
                    Vrm10MotionSnapshotProvider>();
            }
        }

        private void NotifyCharacterLoaded(
            Vrm10Instance instance)
        {
            var subscribers =
                CharacterLoaded;

            if (subscribers == null)
            {
                return;
            }

            foreach (Action<Vrm10Instance> subscriber in
                     subscribers.GetInvocationList())
            {
                try
                {
                    subscriber(
                        instance);
                }
                catch (Exception exception)
                {
                    Debug.LogException(
                        exception,
                        this);
                }
            }
        }

        private void NotifyCharacterUnloaded()
        {
            var subscribers =
                CharacterUnloaded;

            if (subscribers == null)
            {
                return;
            }

            foreach (Action subscriber in
                     subscribers.GetInvocationList())
            {
                try
                {
                    subscriber();
                }
                catch (Exception exception)
                {
                    Debug.LogException(
                        exception,
                        this);
                }
            }
        }

        private void NotifyLoadFailed(
            Exception loadException)
        {
            var subscribers =
                LoadFailed;

            if (subscribers == null)
            {
                return;
            }

            foreach (Action<Exception> subscriber in
                     subscribers.GetInvocationList())
            {
                try
                {
                    subscriber(
                        loadException);
                }
                catch (Exception exception)
                {
                    Debug.LogException(
                        exception,
                        this);
                }
            }
        }

        private static bool IsServiceAlive(
            object service)
        {
            if (service == null)
            {
                return false;
            }

            return service is UnityEngine.Object unityObject
                ? unityObject != null
                : true;
        }

        private static void DestroyLoaded(
            Vrm10Instance instance)
        {
            if (instance == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(instance.gameObject);
            }
            else
            {
                DestroyImmediate(instance.gameObject);
            }
        }

        private void OnDestroy()
        {
            CancelPendingLoad();

            if (Current != null)
            {
                DestroyLoaded(Current);
                Current = null;
            }
        }
    }
}
