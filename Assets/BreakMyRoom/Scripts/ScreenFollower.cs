using UnityEngine;

namespace BreakMyRoom
{
    // 타이틀·결과 화면을 도구 HUD처럼 머리 yaw에 따라 정면에 유지
    public sealed class ScreenFollower : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform head;

        [Header("Placement")]
        [SerializeField] private float forwardDistance = 1f;
        [SerializeField] private float heightBelowHead = 0.10f;

        [Header("Follow")]
        [SerializeField] private float yawDeadZone = 15f;
        [SerializeField] private float yawFollowSpeed = 6f;

        private float heading;
        private bool hasHeading;

        public void Configure(Transform headTransform)
        {
            head = headTransform;
            hasHeading = false;
        }

        // 화면이 켜질 때 바로 정면에 배치
        private void OnEnable()
        {
            hasHeading = false;
        }

        private void LateUpdate()
        {
            if (head == null) return;

            Vector3 horizontalForward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            // 위·아래를 볼 때 수평 투영 불안정으로 인한 화면 회전 방지
            if (horizontalForward.sqrMagnitude > 0.04f)
            {
                float headHeading = Mathf.Atan2(horizontalForward.x, horizontalForward.z) * Mathf.Rad2Deg;
                if (hasHeading == false)
                {
                    heading = headHeading;
                    hasHeading = true;
                }
                else
                {
                    float difference = Mathf.DeltaAngle(heading, headHeading);
                    if (Mathf.Abs(difference) > yawDeadZone)
                    {
                        float targetHeading = headHeading - Mathf.Sign(difference) * yawDeadZone;
                        heading = Mathf.LerpAngle(heading, targetHeading, 1f - Mathf.Exp(-yawFollowSpeed * Time.deltaTime));
                    }
                }
            }
            if (hasHeading == false) return;

            Quaternion rotation = Quaternion.Euler(0f, heading, 0f);
            Vector3 position = head.position + rotation * new Vector3(0f, -heightBelowHead, forwardDistance);
            transform.SetPositionAndRotation(position, rotation);
        }
    }
}
