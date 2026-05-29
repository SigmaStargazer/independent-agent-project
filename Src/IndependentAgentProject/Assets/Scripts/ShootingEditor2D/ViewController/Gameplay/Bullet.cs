using FrameworkDesign;
using UnityEngine;


namespace ShootingEditor2D
{
    public class Bullet : ShootingEditor2DController
    {
        private Rigidbody2D mRigidbody2D;

        private void Awake()
        {
            mRigidbody2D = GetComponent<Rigidbody2D>();

            // 5ÃëºóÏú»Ù
            Destroy(gameObject, 5);
        }
        void Start()
        {
            mRigidbody2D.velocity = Vector2.right * 10 * Mathf.Sign(transform.localScale.x);
        }

        private void OnCollisionStay2D(Collision2D other)
        {
            if(other.gameObject.CompareTag("Enemy"))
            {
                this.SendCommand<KillEnemyCommand>();

                Destroy(other.gameObject);
                //other.gameObject.GetComponent<Enemy>().OnHit();
                Destroy(gameObject);
            }
        }
    }

}
