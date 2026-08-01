using System.Collections;
using UnityEngine;


public class LadderSystem : MonoBehaviour
{
    [SerializeField] float climbSpeed = 2f;
    [SerializeField] float snapSpeed = 8f; 

    [Header("Animator")]
    [SerializeField] string climbAnimName = "LadderClimb";
    [SerializeField] string climbSpeedParam = "climbSpeed";
    [SerializeField] string exitTopAnimName = "LadderExitTop";
    [SerializeField] string locomotionAnimName = "Locomotion";

    Animator animator;
    PlayerController playerController;

    Ladder currentLadder;
    bool inLadderTrigger;
    bool isClimbing;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        playerController = GetComponent<PlayerController>();
    }

    private void Update()
    {
        if (inLadderTrigger && !isClimbing && Input.GetKeyDown(KeyCode.F))
        {
            StartCoroutine(ClimbLadder(currentLadder));
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        var ladder = other.GetComponent<Ladder>();
        if (ladder == null) return;

        currentLadder = ladder;
        inLadderTrigger = true;
    }

    private void OnTriggerExit(Collider other)
    {
        var ladder = other.GetComponent<Ladder>();
        if (ladder == null || ladder != currentLadder) return;

        inLadderTrigger = false;
        if (!isClimbing)
            currentLadder = null;
    }

    IEnumerator ClimbLadder(Ladder ladder)
    {
        isClimbing = true;
        playerController.SetControl(false);

       
        var targetRotation = Quaternion.LookRotation(-ladder.Forward);
        var rail = ladder.RailPosition;
        var targetPos = new Vector3(rail.x, transform.position.y, rail.z);

        while (Vector3.Distance(transform.position, targetPos) > 0.05f)
        {
            transform.position = Vector3.MoveTowards(transform.position, targetPos, snapSpeed * Time.deltaTime);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, 720f * Time.deltaTime);
            yield return null;
        }
        transform.rotation = targetRotation;

        animator.CrossFade(climbAnimName, 0.2f);

        while (isClimbing)
        {
            float v = Input.GetAxis("Vertical");
            animator.SetFloat(climbSpeedParam, v, 0.1f, Time.deltaTime);

            var pos = transform.position;
            pos.y += v * climbSpeed * Time.deltaTime;
            // keep locked to the rail horizontally
            pos.x = rail.x;
            pos.z = rail.z;
            transform.position = pos;

            if (v > 0 && transform.position.y >= ladder.TopY)
            {
                yield return StartCoroutine(ExitAtTop());
                break;
            }

            if (v < 0 && transform.position.y <= ladder.BottomY)
            {
                ExitLadder();
                break;
            }

            if (Input.GetKeyDown(KeyCode.F))
            {
                ExitLadder();
                break;
            }

            yield return null;
        }
    }

    IEnumerator ExitAtTop()
    {
        animator.CrossFade(exitTopAnimName, 0.2f);
        yield return null;

        var state = animator.GetCurrentAnimatorStateInfo(0);
        yield return new WaitForSeconds(state.length);

        ExitLadder();
    }

    void ExitLadder()
    {
        isClimbing = false;
        currentLadder = null;
        animator.SetFloat(climbSpeedParam, 0f);
        animator.CrossFade(locomotionAnimName, 0.2f);
        playerController.SetControl(true);
    }
}