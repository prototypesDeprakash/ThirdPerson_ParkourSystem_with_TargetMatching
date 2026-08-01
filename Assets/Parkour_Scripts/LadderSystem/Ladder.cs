using UnityEngine;


public class Ladder : MonoBehaviour
{
    [Tooltip("Point at the base of the ladder")]
    [SerializeField] Transform bottomPoint;

    [Tooltip("Point at the top of the ladder")]
    [SerializeField] Transform topPoint;

    [Tooltip("How far in front of the ladder")]
    [SerializeField] float climbOffset = 0.5f;

    public Transform BottomPoint => bottomPoint;
    public Transform TopPoint => topPoint;
    public Vector3 Forward => transform.forward;

   
    public Vector3 RailPosition => transform.position + transform.forward * climbOffset;

    public float TopY => topPoint.position.y;
    public float BottomY => bottomPoint.position.y;

    private void OnDrawGizmos()
    {
        if (bottomPoint == null || topPoint == null) return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(bottomPoint.position, topPoint.position);
        Gizmos.DrawWireSphere(bottomPoint.position, 0.1f);
        Gizmos.DrawWireSphere(topPoint.position, 0.1f);

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(bottomPoint.position, RailPosition + Vector3.down * (bottomPoint.position.y - RailPosition.y));
    }
}
