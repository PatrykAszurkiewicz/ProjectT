using UnityEngine;

public class AugmentEffectHandler : MonoBehaviour
{
    private ObstacleGenerator obstacleGenerator;

    private void Awake()
    {
        obstacleGenerator = FindAnyObjectByType<ObstacleGenerator>();
    }

    public void ApplyAugmentEffect(int augmentId)
    {
        // example: augment ID 3 = Obstacles generation
        switch (augmentId)
        {
            case 3:
                if (obstacleGenerator != null)
                {
                    //Debug.Log("Applying Obstacle Generation augment!");
                    obstacleGenerator.GenerateObstacles();
                }
                break;

            // add more augments here:
            // case 5: Player gets shield or smth etc

            default:
                Debug.Log($"No special effect defined for augment {augmentId}");
                break;
        }
    }
}
