namespace VCR.Runtime.EventRuntime
{
    public static class EventActionTypes
    {
        public const string EnvironmentSetState =
            "environment.set_state";

        public const string CameraSetFieldOfView =
            "camera.set_fov";

        public const string MaterialSetFloat =
            "material.set_float";

        public const string MaterialSetInt =
            "material.set_int";

        public const string MaterialSetBool =
            "material.set_bool";

        public const string MaterialSetColor =
            "material.set_color";

        public const string MaterialSetVector =
            "material.set_vector";

        public const string MaterialSetTexture =
            "material.set_texture";

        public const string MaterialSetShader =
            "material.set_shader";

        public const string MaterialApplyPreset =
            "material.apply_preset";

        public const string ExpressionSet =
            "expression.set";

        public const string MotionPoseWeight =
            "motion.pose_weight";

        public const string MotionPlay =
            "motion.play";

        public const string MotionRelease =
            "motion.release";

        public const string EffectPlay =
            "effect.play";

        public const string EffectStop =
            "effect.stop";

        public const string AudioPlay =
            "audio.play";

        public const string AudioStop =
            "audio.stop";

        public const string AppearanceSetPreset =
            "appearance.set_preset";

        public const string AppearanceSetOutfit =
            "appearance.set_outfit";

        public const string AppearanceSetAccessory =
            "appearance.set_accessory";

        public const string AppearanceClearAccessory =
            "appearance.clear_accessory";

        public const string AppearanceRestoreDefault =
            "appearance.restore_default";
    }
}
