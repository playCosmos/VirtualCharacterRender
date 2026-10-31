using UnityEngine;
using UnityEngine.UIElements;

namespace VCR.Runtime.UI
{
    public sealed partial class WorkspaceToolkitShell
    {
        // Tracking-camera video is sensitive and is never discovered from the
        // scene's render camera. A tracking integration must explicitly bind it.
        // UI reconstruction preserves the user's current opt-in, but disable
        // and re-enable always start hidden.
        private Texture _workspaceTrackingCameraTexture;
        private Image _workspaceTrackingCameraImage;
        private Label _workspaceTrackingCameraPrivacyLabel;
        private Button _workspaceTrackingCameraToggle;
        private bool _workspaceTrackingCameraRequested;

        private void ResetWorkspaceTrackingCameraPreview()
        {
            if (_workspaceTrackingCameraImage != null)
                _workspaceTrackingCameraImage.image = null;
            _workspaceTrackingCameraTexture = null;
            _workspaceTrackingCameraImage = null;
            _workspaceTrackingCameraPrivacyLabel = null;
            _workspaceTrackingCameraToggle = null;
            _workspaceTrackingCameraRequested = false;
        }

        /// <summary>
        /// Binds a tracking camera texture supplied explicitly by a tracking
        /// integration. Binding alone never reveals the image; the operator
        /// must opt in using the Tracking panel's Show button.
        /// </summary>
        public void BindTrackingCameraPreviewTexture(Texture texture)
        {
            _workspaceTrackingCameraTexture = texture;
            RefreshWorkspaceTrackingCameraPreview();
        }

        private void BuildWorkspaceTrackingCameraPreview(VisualElement container)
        {
            var section = new VisualElement { name = "vcr-tracking-camera-preview" };
            section.style.marginTop = 4;
            section.style.marginBottom = 4;
            _workspaceTrackingCameraToggle = new Button(() =>
            {
                _workspaceTrackingCameraRequested = !_workspaceTrackingCameraRequested;
                RefreshWorkspaceTrackingCameraPreview();
            });
            section.Add(_workspaceTrackingCameraToggle);
            _workspaceTrackingCameraPrivacyLabel =
                new Label("Tracking-camera preview is hidden by default.");
            _workspaceTrackingCameraPrivacyLabel.style.whiteSpace = WhiteSpace.Normal;
            section.Add(_workspaceTrackingCameraPrivacyLabel);
            _workspaceTrackingCameraImage = new Image
            {
                name = "vcr-tracking-camera-image",
                scaleMode = ScaleMode.ScaleToFit
            };
            _workspaceTrackingCameraImage.style.height = 144;
            _workspaceTrackingCameraImage.style.minHeight = 96;
            section.Add(_workspaceTrackingCameraImage);
            container.Add(section);
            RefreshWorkspaceTrackingCameraPreview();
        }

        private void RefreshWorkspaceTrackingCameraPreview()
        {
            if (_workspaceTrackingCameraToggle != null)
            {
                // A stacked 1024px workspace divides the bottom dock into
                // narrow panels. The old full caption overflowed Tracking's
                // scroll viewport, forcing a horizontal scrollbar and making
                // the button label unreadable. Preserve the full wording in
                // the wide compact-tab layout and in the tooltip.
                var fullLabel = _workspaceTrackingCameraRequested
                    ? "Hide tracking camera preview"
                    : "Show tracking camera preview";
                // Auto-tabs also give Tracking the full dock width in
                // narrow-but-tall windows, so no abbreviated label is needed.
                _workspaceTrackingCameraToggle.text =
                    (_usingCompactHeight || _usingNarrowWidth)
                    ? fullLabel
                    : (_workspaceTrackingCameraRequested
                        ? "Hide preview" : "Show preview");
                _workspaceTrackingCameraToggle.tooltip = fullLabel;
            }

            // Clearing the Image's source is intentional; hiding only its
            // container would retain a sensitive texture as drawable UI data.
            var visible = _workspaceTrackingCameraRequested &&
                _workspaceTrackingCameraTexture != null;
            if (_workspaceTrackingCameraImage != null)
            {
                _workspaceTrackingCameraImage.image =
                    visible ? _workspaceTrackingCameraTexture : null;
                _workspaceTrackingCameraImage.style.display =
                    visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (_workspaceTrackingCameraPrivacyLabel != null)
            {
                _workspaceTrackingCameraPrivacyLabel.text =
                    !_workspaceTrackingCameraRequested
                        ? "Camera video hidden. Showing requires your explicit action."
                        : _workspaceTrackingCameraTexture == null
                            ? "Preview requested, but no tracking-camera texture is connected."
                            : "Tracking-camera video is visible. Hide it when not needed.";
            }
        }
    }
}
