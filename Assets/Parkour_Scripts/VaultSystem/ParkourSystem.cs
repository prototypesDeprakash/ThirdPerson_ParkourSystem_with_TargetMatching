using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ParkourSystem : MonoBehaviour
{
    EnvironmentScanner environmentScanner;
    Animator animator;
    PlayerController playerController;


    [SerializeField] List<ParkourAction> parkourActions;
    bool inAction;


    private void Awake()
    {
        environmentScanner = GetComponent<EnvironmentScanner>();
        animator = GetComponent<Animator>();
        playerController = GetComponent<PlayerController>();
    }

    private void Update()
    {
        if (Input.GetButton("Jump") && !inAction)
        {


            var hitData = environmentScanner.ObstacleCheck();
            if (hitData.forwardHitFound)
            {
                foreach (var action in parkourActions)
                {
                    if(action.checkIfPossible(hitData, transform))
                    {
                        StartCoroutine(DoParkourAction(action));
                        break;
                    }
                }
                //StartCoroutine(DoParkourAction());
            }
            else
            {

                // No parkour action found
               StartCoroutine(DoNormalJump());
            }


        }
    }
    IEnumerator DoNormalJump()
    {
        inAction = true;
        playerController.SetControl(false);

        //playerController.Jump();
        animator.CrossFade("Jump", 0.1f);

        yield return null;

        while (animator.GetCurrentAnimatorStateInfo(0).IsName("Jump"))
        {
            yield return null;
        }

        playerController.SetControl(true);
        inAction = false;
    }

    IEnumerator DoParkourAction(ParkourAction action)
    {
        inAction = true;
        playerController.SetControl(false);

        animator.CrossFade(action.AnimName, 0.2f);
        yield return null;
        var animState = animator.GetCurrentAnimatorStateInfo(0);
        if(!animState.IsName(action.AnimName))
        {
            //Debug.LogError("The parkour animation name is wrong!!");
        }
        float timer = 0f;
        while(timer < animState.length)
        {
            timer += Time.deltaTime;
            //rotate the player towards the obstacle

            if (action.RotateToObstacle)
            {
              transform.rotation= Quaternion.RotateTowards(transform.rotation, action.TargetRotation, playerController.RotationSpeed * Time.deltaTime);
            }
            if (action.EnableTargetMatching)
            {
                MatchTarget(action);
            }

            if (animator.IsInTransition(0) && timer>0.5f) 
            {
                break;
            }
            
            yield return null;
        }
        yield return new WaitForSeconds(action.PostActionDelay);
        playerController.SetControl(true);
        inAction = false;
    }
    void MatchTarget(ParkourAction action)
    {
        if (animator.isMatchingTarget) return;
        
            
        
        animator.MatchTarget(action.MatchPos, transform.rotation, action.MatchBodyPart,
        new MatchTargetWeightMask(action.MatchPosWeight, 0), 
        action.MatchStartTime, action.MatchTargetTime);
    }
}
