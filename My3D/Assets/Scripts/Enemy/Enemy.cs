using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class Enemy : MonoBehaviour
{
    public GameObject trackingTarget;
    private NavMeshAgent agent;
    private Animator animator;
    public float maxA = 5f;//最大加速度
    private float attackRange;//攻击距离
    private HealthPoint healthPoint;
    private float patrolRange = Settings.enemyPatrolRange;
    private float patrolTime = 5f;
    private float startPatrolTime;
    private float followSpeed, patrolSpeed=1;
    private Vector3 originPos, patrolPos;
    private PlayerState playerState;
    private void Start()
    {
        agent=GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
        attackRange=agent.stoppingDistance;
        healthPoint = GetComponent<HealthPoint>();
        startPatrolTime=Time.time;
        followSpeed = agent.speed;
        originPos = transform.position;
        playerState = PlayerState.Alive;
    }

    private void Update()
    {

        if (CheckDeath()) {
            return;
        
        }
        


        float distance = Vector3.Distance(trackingTarget.transform.position, transform.position);

        if (distance < patrolRange && playerState==PlayerState.Alive)
        {
          
            agent.speed = followSpeed;
            //导航部分
            Navigate(trackingTarget.transform.position);
            //攻击部分
            if (!animator.GetCurrentAnimatorStateInfo(0).IsName("Attack"))
            Attack(distance);
        }
        else
        {
            agent.speed = patrolSpeed;
            //巡逻
            Patrol();
        }
       
    }

    private void OnEnable()
    {
        EventHandler.PlayerDeathEvent += OnPlayerDeathEvent;
    }

    private void OnDisable()
    {
        EventHandler.PlayerDeathEvent -= OnPlayerDeathEvent;
    }

    private void OnPlayerDeathEvent(PlayerState state)
    {
      playerState= state;
    }

    private void OnCollisionEnter(Collision collision)
    {
        var relativePos = collision.contacts[0].point - transform.position;
        var relativeAngle=Mathf.Atan2(relativePos.x,relativePos.z)/Mathf.PI*180;
        animator.SetTrigger("BeAttacked");
        animator.SetFloat("AttackAngle", relativeAngle);
    }
    private void Hit()
    {


    }
    private void Navigate(Vector3 target)
    {
        float distance = Vector3.Distance(target, transform.position);

        if (distance > agent.stoppingDistance && !animator.GetCurrentAnimatorStateInfo(0).IsName("BeAttacked"))

            agent.SetDestination(target);

        else if (distance <= agent.stoppingDistance)
            agent.velocity = Vector3.zero;


        var speed = agent.velocity.magnitude;

        var rotationDirection = Quaternion.Inverse(transform.rotation) * agent.desiredVelocity;

        var direction = Mathf.Atan2(rotationDirection.x, rotationDirection.z) / Mathf.PI;


        agent.acceleration = Mathf.Min(distance, maxA);

        animator.SetFloat("Speed", speed * 2 / agent.speed);
        animator.SetFloat("Direction", direction);

    }
    private void Attack(float distance)
    {
        if (distance < attackRange && !animator.GetCurrentAnimatorStateInfo(0).IsName("BeAttacked"))
        {
            transform.LookAt(trackingTarget.transform);
            animator.SetBool("IsAttack", true);
            BroadcastMessage("AttackStart");
        }
        else
        {
            animator.SetBool("IsAttack", false);
            BroadcastMessage("AttackEnd");
        }


    }
    private bool CheckDeath() {

        if (healthPoint.hp <= 0)
        {
            animator.SetBool("Death", true);
            agent.enabled = false;
            this.enabled = false;
            StartCoroutine(ClearBody());
            return true;
        }
    return false;
    }

    private IEnumerator ClearBody()
    {

        yield return new WaitForSeconds(3f);
        Destroy(gameObject);
    }

    private void Patrol()
    {
        if (animator.GetFloat("Speed") == 0 || Time.time - startPatrolTime >= patrolTime)
        {

            float x = originPos.x + Random.Range(-patrolRange, patrolRange);
            float y = originPos.y;
            float z = originPos.z+ Random.Range(-patrolRange,patrolRange);
            patrolPos = new Vector3(x, y, z);
            startPatrolTime = Time.time;

        }
        Navigate(patrolPos);
        //设置非攻击姿态
        animator.SetBool("IsAttack", false);
        BroadcastMessage("AttackEnd");
    }

    
}
