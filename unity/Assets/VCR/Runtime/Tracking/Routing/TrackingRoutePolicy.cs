using System;
using UnityEngine;

namespace VCR.Runtime.Tracking.Routing
{
    /// <summary>
    /// One-performer routing policy.
    ///
    /// Provider references remain scene wiring. Source priority is data: the
    /// first matching usable source kind in FacePriorityOrder wins. This keeps
    /// current ARKit/MediaPipe defaults while allowing deterministic policy
    /// changes without introducing a generic multi-person routing graph.
    /// </summary>
    [Serializable]
    public sealed class TrackingRoutePolicy
    {
        [SerializeField]
        private TrackingSourceKind[] facePriorityOrder =
        {
            TrackingSourceKind.ArKitFace,
            TrackingSourceKind.MediaPipeFaceWebcam
        };

        [SerializeField]
        private TrackingSourceKind[] expressionPriorityOrder =
        {
            TrackingSourceKind.Vmc,
            TrackingSourceKind.AudioFallback
        };

        public TrackingSourceKind[] FacePriorityOrder =>
            (TrackingSourceKind[])
                RuntimeFacePriorityOrder.Clone();

        public TrackingSourceKind[] ExpressionPriorityOrder =>
            (TrackingSourceKind[])
                RuntimeExpressionPriorityOrder.Clone();

        internal TrackingSourceKind[]
            RuntimeFacePriorityOrder
        {
            get
            {
                if (facePriorityOrder == null ||
                    facePriorityOrder.Length == 0)
                {
                    facePriorityOrder =
                        DefaultFacePriority();
                }

                return facePriorityOrder;
            }
        }

        internal TrackingSourceKind[]
            RuntimeExpressionPriorityOrder
        {
            get
            {
                if (expressionPriorityOrder == null ||
                    expressionPriorityOrder.Length == 0)
                {
                    expressionPriorityOrder =
                        DefaultExpressionPriority();
                }

                return expressionPriorityOrder;
            }
        }

        public int GetFacePriority(
            TrackingSourceKind kind)
        {
            var order =
                RuntimeFacePriorityOrder;

            for (var i = 0; i < order.Length; i++)
            {
                if (order[i] == kind)
                {
                    return i;
                }
            }

            return int.MaxValue;
        }

        public int GetExpressionPriority(
            TrackingSourceKind kind)
        {
            var order =
                RuntimeExpressionPriorityOrder;

            for (var i = 0; i < order.Length; i++)
            {
                if (order[i] == kind)
                {
                    return i;
                }
            }

            return int.MaxValue;
        }

        public void SetFacePriorityOrder(
            params TrackingSourceKind[] order)
        {
            if (order == null ||
                order.Length == 0)
            {
                facePriorityOrder =
                    DefaultFacePriority();
                return;
            }

            facePriorityOrder =
                (TrackingSourceKind[])
                order.Clone();
        }

        public void SetExpressionPriorityOrder(
            params TrackingSourceKind[] order)
        {
            if (order == null ||
                order.Length == 0)
            {
                expressionPriorityOrder =
                    DefaultExpressionPriority();
                return;
            }

            expressionPriorityOrder =
                (TrackingSourceKind[])
                order.Clone();
        }

        public static TrackingRoutePolicy CreateDefault()
        {
            return new TrackingRoutePolicy();
        }

        private static TrackingSourceKind[]
            DefaultFacePriority()
        {
            return new[]
            {
                TrackingSourceKind.ArKitFace,
                TrackingSourceKind.MediaPipeFaceWebcam
            };
        }

        private static TrackingSourceKind[]
            DefaultExpressionPriority()
        {
            return new[]
            {
                TrackingSourceKind.Vmc,
                TrackingSourceKind.AudioFallback
            };
        }
    }
}
