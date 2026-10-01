# Local Packages

Large third-party binary packages are not committed here.

Before opening the Unity project for the first time, run one of:

Windows PowerShell:

```powershell
./tools/bootstrap-mediapipe.ps1
```

macOS/Linux:

```bash
./tools/bootstrap-mediapipe.sh
```

The scripts download the pinned MediaPipeUnityPlugin 0.16.3 tarball and verify its SHA-256 digest.

Expected local file:

```text
unity/Packages/LocalPackages/com.github.homuler.mediapipe-0.16.3.tgz
```

The project manifest references this tarball through a local `file:` dependency.
