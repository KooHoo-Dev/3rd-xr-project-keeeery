using UnityEngine;

namespace BreakMyRoom
{
    [DefaultExecutionOrder(-100)]
    public sealed class ToolBelt : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform head;

        [Header("Placement")]
        [SerializeField] private float forwardDistance = 0.37f;
        [SerializeField] private float heightBelowHead = 0.60f;

        [Header("Follow")]
        [SerializeField] private float yawDeadZone = 12f;
        [SerializeField] private float yawFollowSpeed = 12f;

        private float heading;
        private bool hasHeading;

        public void Configure(Transform headTransform)
        {
            head = headTransform;
            hasHeading = false;
        }

        // 결과 화면 중 돌아섰어도 새 판에서 HUD를 바로 앞에 배치
        private void OnEnable()
        {
            hasHeading = false;
        }

        private void LateUpdate()
        {
            if (head == null) return;

            Vector3 horizontalForward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            // 발밑을 볼 때 수평 투영 불안정으로 인한 도구 뒤집힘 방지
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

            Quaternion rotation = Quaternion.Euler(0f, heading, 0f);
            Vector3 position = head.position + rotation * new Vector3(0f, -heightBelowHead, forwardDistance);
            transform.SetPositionAndRotation(position, rotation);
        }
    }
}
