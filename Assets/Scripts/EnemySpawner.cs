using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using static GameController;

public class EnemySpawner : MonoBehaviour
{
    private GameController gc;
    [SerializeField]private GameObject trianglePrefab;
    // Start is called before the first frame update
    public void Initialize()
    {
        gc = GetComponent<GameController>();
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void SpawnRemainingEnemies()
    {
        if (gc.trianglesSpawned < gc.totalTrianglesToSpawn)
        {
            int spawnCount = Mathf.Min(6, gc.totalTrianglesToSpawn - gc.trianglesSpawned);
            SpawnEnemies(spawnCount);

            if (gc.trianglesSpawned >= gc.totalTrianglesToSpawn)
            {
                gc.isSpawningEnemies = false; // Unset the flag once all triangles are spawned
            }

            gc.UpdateChasingTriangle();
        }
    }

    private int nameCounter = 0;
    void SpawnEnemies(int spawnCount)
    {
        HashSet<Vector2Int> occupiedPositions = new HashSet<Vector2Int>();

        occupiedPositions.Add(gc.player.position);

        for (int i = 0; i < spawnCount; i++)
        {
            Vector2Int spawnPosition = GetOutsideSpawnPosition(occupiedPositions);
            if (spawnPosition != Vector2Int.zero)
            {
                GameObject triangleObject = Instantiate(trianglePrefab, new Vector2(spawnPosition.x * gc.tileSize, spawnPosition.y * gc.tileSize), GetRoToC(spawnPosition));
                Triangle triangle = triangleObject.GetComponent<Triangle>();
                triangle.Initialize(spawnPosition, gc, beatTimer);  // Use the Initialize method
                gc.enemies.Add(triangle);
                occupiedPositions.Add(spawnPosition);
                triangle.name = ++nameCounter + "Triangle";

                // Ensure the triangle's first two moves are towards the inside of the grid
                triangle.SetInitialMoves();

                gc.trianglesSpawned++;
            }
        }
        gc.gridBoundsFlag = true;
    }

    public Quaternion GetRoToC(Vector2 initial)
    {
        if(initial.y >= gridBounds[3])
        {
            return Quaternion.Euler(0,0,180);
        }
        else if(initial.x < 4)
        {
            return Quaternion.Euler(0,0,270);
        }
        else if(initial.x > 4)
        {
            return Quaternion.Euler(0,0,90);
        }
        else return Quaternion.Euler(0,0,0);
    }
    /// <summary>Spawns a Triangle from a snapshot taken during enemy evacuation.
    /// Sets powerLevel before Initialize so UpdateColor() gets the right colour,
    /// then overrides health after Initialize to restore the original value.</summary>
    public void SpawnSnapshotTriangle(GameController.EnemySnapshot snap, HashSet<Vector2Int> occupied)
    {
        Vector2Int pos = GetOutsideSpawnPosition(occupied);
        if (pos == Vector2Int.zero) return;
        GameObject go = Instantiate(trianglePrefab,
            new Vector2(pos.x * gc.tileSize, pos.y * gc.tileSize), GetRoToC(pos));
        Triangle t = go.GetComponent<Triangle>();
        // Set powerLevel BEFORE Initialize so UpdateColor() uses the correct value
        t.powerLevel = snap.powerLevel;
        t.Initialize(pos, gc, beatTimer);
        // Override health AFTER Initialize (Initialize resets health from powerLevel)
        t.health = snap.health;
        gc.enemies.Add(t);
        occupied.Add(pos);
        t.name = ++nameCounter + "Triangle(Restored)";
        t.SetInitialMoves();
        gc.gridBoundsFlag = true;
    }
    public Vector2Int GetOutsideSpawnPosition(HashSet<Vector2Int> occupiedPositions)
    {
        List<Vector2Int> possiblePositions = new List<Vector2Int>();
        Vector2Int playerPos = gc.player.position;

        // Generate positions outside the grid, excluding the bottom side
        for (int y = 1; y < gridBounds[3]; y++)
        {
            if (playerPos.x != gridBounds[0])
            {
                Vector2Int leftPosition = new Vector2Int(gridBounds[0]-1, y); // Left side
                if (!occupiedPositions.Contains(leftPosition))
                    possiblePositions.Add(leftPosition);
            }

            if (playerPos.x != gridBounds[1] - 1)
            {
                Vector2Int rightPosition = new Vector2Int(gridBounds[1], y); // Right side
                if (!occupiedPositions.Contains(rightPosition))
                    possiblePositions.Add(rightPosition);
            }
        }

        for (int x = 0; x < gridBounds[1]; x++)
        {
            if (playerPos.y != gridBounds[3] - 1)
            {
                Vector2Int topPosition = new Vector2Int(x, gridBounds[3]); // Top side
                if (!occupiedPositions.Contains(topPosition))
                    possiblePositions.Add(topPosition);
            }
        }

        if (possiblePositions.Count == 0)
        {
            return Vector2Int.zero; // No valid position found
        }

        int randIndex = UnityEngine.Random.Range(0, possiblePositions.Count);
        return possiblePositions[randIndex];
    }
}
