using UnityEngine;

namespace ChromaGLS.FerrofluidBundle
{
    public sealed class FerrofluidValidationContacts : MonoBehaviour
    {
        public Collider Floor = null!;
        public Collider Wall = null!;
        public int FloorContacts;
        public int WallContacts;

        private void OnCollisionEnter(Collision collision) => Record(collision);

        private void OnCollisionStay(Collision collision) => Record(collision);

        private void Record(Collision collision)
        {
            if (collision.collider == Floor)
                FloorContacts++;
            if (collision.collider == Wall)
                WallContacts++;
        }
    }
}
