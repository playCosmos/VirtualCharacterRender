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

        public const string ExpressionSet =
            "expression.set";

        public const string MotionPoseWeight =
            "motion.pose_weight";
    }
}
