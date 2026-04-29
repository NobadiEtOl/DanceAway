using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;


using static GameController;
public class GridController : MonoBehaviour
{
    private GameController gc;
    [SerializeField]public GameObject tilePrefab; 
    // Start is called before the first frame update
    public void Initialize()
    {
        gc = GetComponent<GameController>();
        gridBounds.Add(0);
        gridBounds.Add(gc.width);
        gridBounds.Add(0);
        gridBounds.Add(gc.height);

    }

    // Update is called once per frame
    void Update()
    {

    }

    public void InitializeGrid()
    {
        GameObject gridParent = new GameObject("Grid");

        for (int x = 0; x < gc.width; x++)
        {
            for (int y = 0; y < gc.height; y++)
            {
                grid[x, y] = Instantiate(tilePrefab, new Vector2(x * gc.tileSize, y * gc.tileSize), Quaternion.identity);
                grid[x, y].transform.localScale = new Vector3(gc.tileSize, gc.tileSize, 1);
                grid[x, y].name = $"Tile_{x}_{y}";
                if ((x + y) % 2 == 1) grid[x, y].GetComponent<SpriteRenderer>().color = Color.cyan;
                grid[x,y].transform.parent = gridParent.transform;
            }
        }
    }

    private int maxX;
    private int maxY;
    private int minX;
    private int minY;

    public void ChangeGridBounds()
    {
        if (gridBounds.Count < 4)
        {
            Debug.LogError("GridBounds list does not contain enough elements!");
            return;
        }

        if(gc.enemies.Count > 30)
        {
            gridBounds[0] = 0;
            gridBounds[1] = 9; 
            gridBounds[2] = 0; 
            gridBounds[3] = 9;
            StartCoroutine(gc.ChangeCameraOrthoSize(60));
        }
        else if(gc.enemies.Count > 18)
        {
            gridBounds[0] = 1;
            gridBounds[1] = 8; 
            gridBounds[2] = 1; 
            gridBounds[3] = 8;
            StartCoroutine(gc.ChangeCameraOrthoSize(50));
        }

        else if(gc.enemies.Count > 3)
        {
            //??????? Her şeyin 3 e 3 den büyük olduğuna emin olmak lazım
            gridBounds[0] = 2;
            gridBounds[1] = 7; 
            gridBounds[2] = 2; 
            gridBounds[3] = 7;
            StartCoroutine(gc.ChangeCameraOrthoSize(35));
        }
        
        else if(gc.enemies.Count > 0)
        {
            gridBounds[0] = 3;
            gridBounds[1] = 6; 
            gridBounds[2] = 3; 
            gridBounds[3] = 6;
            StartCoroutine(gc.ChangeCameraOrthoSize(22));
        }


        gc.player.ChangeGridBounds();
    }

    public void FlashBoundaryTiles()
    {
        int[] next = GetNextBounds();
        if (next == null) return;

        int nMinX = next[0], nMaxX = next[1], nMinY = next[2], nMaxY = next[3];
        Color flashColor = new Color(1f, 0.15f, 0.15f, 1f);

        for (int x = nMinX; x < nMaxX; x++)
        {
            for (int y = nMinY; y < nMaxY; y++)
            {
                if (x == nMinX || x == nMaxX - 1 || y == nMinY || y == nMaxY - 1)
                {
                    grid[x, y].GetComponent<SpriteRenderer>().color = flashColor;
                }
            }
        }
    }

    private int[] GetNextBounds()
    {
        if (gc.enemies.Count > 30)
            return new int[] { 0, 9, 0, 9 };
        else if (gc.enemies.Count > 18)
            return new int[] { 1, 8, 1, 8 };
        else if (gc.enemies.Count > 3)
            return new int[] { 2, 7, 2, 7 };
        else if (gc.enemies.Count > 0)
            return new int[] { 3, 6, 3, 6 };
        return null;
    }

    //Level başlarında grid size ı düzeltmek için kullan.
    public void ResetGridBounds()
    {
        if(gc.levelNo > 30)
        {
            gridBounds[0] = 0;
            gridBounds[1] = 9; 
            gridBounds[2] = 0; 
            gridBounds[3] = 9;
            StartCoroutine(gc.ChangeCameraOrthoSize(60));
        }

        else if(gc.levelNo > 18)
        {
            gridBounds[0] = 1;
            gridBounds[1] = 8; 
            gridBounds[2] = 1; 
            gridBounds[3] = 8;
            StartCoroutine(gc.ChangeCameraOrthoSize(50));
        }

        else if(gc.levelNo > 3)
        {
            //??????? Her şeyin 3 e 3 den büyük olduğuna emin olmak lazım
            gridBounds[0] = 2;
            gridBounds[1] = 7; 
            gridBounds[2] = 2; 
            gridBounds[3] = 7;
            StartCoroutine(gc.ChangeCameraOrthoSize(35));
        }

        else if(gc.levelNo > 0)
        {
            gridBounds[0] = 3;
            gridBounds[1] = 6; 
            gridBounds[2] = 3; 
            gridBounds[3] = 6;
            StartCoroutine(gc.ChangeCameraOrthoSize(22));
        }

        gc.player.ChangeGridBounds();
    }
    /*public void ChangeGridBounds()
    {
        if (gridBounds.Count < 4)
        {
            Debug.LogError("GridBounds list does not contain enough elements!");
            return;
        }

        if(gc.enemies.Count==0)
        {
            gridBounds[0] = 0;
            gridBounds[1] = 7; 
            gridBounds[2] = 0; 
            gridBounds[3] = 7;
            StartCoroutine(gc.ChangeCameraOrthoSize(1.3f));
        }

        else
        {
            maxX=Math.Max(gc.enemies.Max(enemy => enemy.position.x),gc.player.position.x);
            maxY=Math.Max(gc.enemies.Max(enemy => enemy.position.y),gc.player.position.y);
            minX=Math.Min(gc.enemies.Min(enemy => enemy.position.x),gc.player.position.x);
            minY=Math.Min(gc.enemies.Min(enemy => enemy.position.y),gc.player.position.y);

            if(maxX<gc.width-2 && minX>1 && maxY<gc.height-2 && minY>1)
            {
                //??????? Her şeyin 3 e 3 den büyük olduğuna emin olmak lazım
                gridBounds[0] = 2;
                gridBounds[1] = 5; 
                gridBounds[2] = 2; 
                gridBounds[3] = 5;
                StartCoroutine(gc.ChangeCameraOrthoSize(2f));
            }
            else if(maxX<gc.width-1 && minX>0 && maxY<gc.height-1 && minY>0)
            {
                //??????? Her şeyin 3 e 3 den büyük olduğuna emin olmak lazım
                gridBounds[0] = 1;
                gridBounds[1] = 6; 
                gridBounds[2] = 1; 
                gridBounds[3] = 6;
                StartCoroutine(gc.ChangeCameraOrthoSize(1.55f));
            }
            else StartCoroutine(gc.ChangeCameraOrthoSize(1.3f));
        }

        gc.player.ChangeGridBounds();
    }*/
}
