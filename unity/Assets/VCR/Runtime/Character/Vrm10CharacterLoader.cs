using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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

            CancelPendingLoad();

            var generation = ++_loadGeneration;
            var loadCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
            _loadCancellation = loadCancellation;

            Vrm10Instance loaded = null;

            try
            {
                loaded = await Vrm10.LoadPathAsync(
                    path,
                    canLoadVrm0X: true,
                    controlRigGenerationOption:
                        ControlRigGenerationOption.Generate,
                    showMeshes: true,
                    materialGenerator:
                        Vrm10MaterialDescriptorGeneratorUtility
                            .GetValidVrm10MaterialDescriptorGenerator(),
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

                CharacterLoaded?.Invoke(Current);
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

                LoadFailed?.Invoke(exception);
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
            CharacterUnloaded?.Invoke();
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

        public void SetTrackingProvider(
            ITrackingFrameProvider provider)
        {
            trackingProviderBehaviour =
                provider as MonoBehaviour;

            if (Current != null)
            {
                BindTracking(Current, provider);
            }
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
                trackingProviderBehaviour as
                    ITrackingFrameProvider;

            BindTracking(instance, provider);
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

            if (provider != null)
            {
                faceTarget.SetTrackingProvider(provider);
                bodyTarget.SetTrackingProvider(provider);
            }

            if (attachMotionSnapshotProvider &&
                instance.GetComponent<
                    Vrm10MotionSnapshotProvider>() == null)
            {
                instance.gameObject.AddComponent<
                    Vrm10MotionSnapshotProvider>();
            }
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
