using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using static GameController;

public class CrowdController : MonoBehaviour
{
    private GameController gc;
    [SerializeField]private GameObject trianglePrefab;
    [SerializeField]private int xOffset;
    [SerializeField]private int yOffset;
    public GameObject crowdParentLeft;
    public GameObject crowdParentRight;
    public GameObject crowdParentBottom;
    public GameObject crowdParentTop;
    private List<GameObject> crowd;
    private GameObject leftCrowd;
    private GameObject rightCrowd;
    private GameObject bottomCrowd;
    private GameObject topCrowd;
    private Vector3 leftCrowdPos;
    private Vector3 rightCrowdPos;
    private Vector3 bottomCrowdPos;
    private Vector3 topCrowdPos;
    private Vector3 gridMiddle;  
    void Start()
    {
        
    }
    // Initialized after GameController
    public void Initialize(BeatTimer beatTimer,int width,int height,float tileSize)
    {
        /*crowdParentLeft = new GameObject("CrowdLeft");
        crowdParentRight = new GameObject("CrowdRight");
        crowdParentBottom = new GameObject("CrowdBottom");
        crowdParentTop = new GameObject("CrowdTop");*/
        crowd = new List<GameObject>();
        gc = GameObject.Find("GameController").GetComponent<GameController>();
        gridMiddle = new Vector3(gc.tileSize*4.5f,gc.tileSize*4.5f,0);
        //SpawnCrowd(beatTimer,width,height,tileSize);
        GetAllCTriangles(beatTimer);
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void GetAllCTriangles(BeatTimer beatTimer)
    {
        for (int i = 0; i<crowdParentLeft.transform.childCount;i++)
        {
            GameObject temp = crowdParentLeft.transform.GetChild(i).gameObject;
            crowd.Add(temp);
            temp.GetComponent<SpriteRenderer>().sortingOrder = (int)(gridMiddle.x - temp.transform.position.x); 
        }
        for (int i = 0; i<crowdParentRight.transform.childCount;i++)
        {
            GameObject temp = crowdParentRight.transform.GetChild(i).gameObject;
            crowd.Add(temp);
            temp.GetComponent<SpriteRenderer>().sortingOrder = (int)(temp.transform.position.x - gridMiddle.x) + 3;
        }
        for (int i = 0; i<crowdParentBottom.transform.childCount;i++)
        {
            GameObject temp = crowdParentBottom.transform.GetChild(i).gameObject;
            crowd.Add(temp);
            temp.GetComponent<SpriteRenderer>().sortingOrder = (int)(gridMiddle.y-temp.transform.position.y);
        }
        for (int i = 0; i<crowdParentTop.transform.childCount;i++)
        {
            GameObject temp = crowdParentTop.transform.GetChild(i).gameObject;
            crowd.Add(temp);
            temp.GetComponent<SpriteRenderer>().sortingOrder = (int)(temp.transform.position.y-gridMiddle.y);
        }
    
        foreach (GameObject triangle in crowd)
        {
            triangle.GetComponent<cTriangle>().Initialize(beatTimer);
        }

        SelectInitialNodders();

        //Debug.LogError("GetAllCTriangles_finished");
    }


    private List<cTriangle> nodders;
    private List<GameObject> notNodders;
    [Range(0,1)]
    [SerializeField]private float initialNodderPer;
    private void SelectInitialNodders()
    {
        nodders = new List<cTriangle>();
        notNodders = new List<GameObject>(crowd);
        
        // The number of initial nodders according to the crowd number
        int initialNoddernum = (int)(notNodders.Count*initialNodderPer);
        // Randomly activating nodding for some cTriangles.
        for(int i = 0; i<initialNoddernum; i++)
        {
            int random = Random.Range(0, notNodders.Count);
            cTriangle temp = notNodders[random].GetComponent<cTriangle>();
            nodders.Add(temp);
            notNodders.RemoveAt(random);   
        }

        foreach(cTriangle nodder in nodders)
        {
            //nodder.canNod = true;
        }

    }

    // Method to randomly activate more cTriangles to nod
    public void MoreNodders(int more)
    {
        //Debug.LogError("more nodders:" + more);
        if(more>notNodders.Count)more=notNodders.Count;
        for(int i = 0; i<more; i++)
        {
            int random = Random.Range(0, notNodders.Count);
            cTriangle temp = notNodders[random].GetComponent<cTriangle>();
            nodders.Add(temp);
            notNodders.RemoveAt(random);  
        }

        for(int i = nodders.Count-1; i>nodders.Count-more-1; i--)
        {
            nodders[i].canNod = true;
        }
    }
    /// <summary>Moves every crowd member into the nodding state — used to match the max-average lock before Start is pressed.</summary>
    public void MaxNodders()
    {
        MoreNodders(notNodders.Count);

        // Some nodders may already be in the list from initialization.
        // Force-enable all of them so nobody stays idle.
        for (int i = 0; i < nodders.Count; i++)
        {
            nodders[i].canNod = true;
        }
    }

    // Method to randomly deactivate some cTriangles to not nod
    public void LessNodders(int less)
    {
        //Debug.LogError("less nodders:" + less);
        if(less>nodders.Count)less=nodders.Count;
        for (int i = 0; i < less; i++)
        {
            int random = Random.Range(0, nodders.Count);
            cTriangle temp = nodders[random];
            temp.canNod = false;
            temp.NotVibing();
            notNodders.Add(temp.gameObject);
            nodders.RemoveAt(random);
        }
    }

    private bool canResize=true;
    public void ResizeCrowd()
    {
        if(!canResize) return;

        int bound = gc.width - gridBounds[1];
        Vector3[] targetPos = new Vector3[]
        {
            leftCrowdPos + new Vector3(bound * gc.tileSize, 0, 0),
            rightCrowdPos + new Vector3(-bound * gc.tileSize, 0, 0),
            bottomCrowdPos + new Vector3(0, bound * gc.tileSize, 0),
            topCrowdPos + new Vector3(0, -bound * gc.tileSize, 0)
        };

        StartCoroutine(ChangeCrowdPosToTargets(targetPos, cameraTransitionDuration));
    }

    public void ResizeCrowdToTargets(Vector3 leftTarget, Vector3 rightTarget, Vector3 bottomTarget, Vector3 topTarget, float duration = -1f)
    {
        if (!canResize) return;

        float transitionDuration = duration > 0f ? duration : cameraTransitionDuration;
        Vector3[] targetPos = new Vector3[]
        {
            leftTarget,
            rightTarget,
            bottomTarget,
            topTarget
        };

        StartCoroutine(ChangeCrowdPosToTargets(targetPos, transitionDuration));
    }

    public void SetCrowdWorldPositions(Vector3 leftPos, Vector3 rightPos, Vector3 bottomPos, Vector3 topPos)
    {
        leftCrowd.transform.position = leftPos;
        rightCrowd.transform.position = rightPos;
        bottomCrowd.transform.position = bottomPos;
        topCrowd.transform.position = topPos;
    }

    public float cameraTransitionDuration = 1f;
    private IEnumerator ChangeCrowdPosToTargets(Vector3[] targetPos, float duration)
    {
        canResize=false;
        float timePassed = 0f;
        Vector3[] initialPos = new Vector3[]
        {leftCrowd.transform.position,
        rightCrowd.transform.position,
        bottomCrowd.transform.position,
        topCrowd.transform.position};

        if (duration <= 0f)
        {
            SetCrowdWorldPositions(targetPos[0], targetPos[1], targetPos[2], targetPos[3]);
            canResize = true;
            yield break;
        }

        while(timePassed<duration)
        {
            float t = timePassed / duration;
            leftCrowd.transform.position = new Vector3(Mathf.Lerp(initialPos[0].x, targetPos[0].x, t), leftCrowd.transform.position.y, 0);
            rightCrowd.transform.position = new Vector3(Mathf.Lerp(initialPos[1].x, targetPos[1].x, t), rightCrowd.transform.position.y, 0);
            bottomCrowd.transform.position = new Vector3(bottomCrowd.transform.position.x, Mathf.Lerp(initialPos[2].y, targetPos[2].y, t), 0);
            topCrowd.transform.position = new Vector3(topCrowd.transform.position.x, Mathf.Lerp(initialPos[3].y, targetPos[3].y, t), 0);
            timePassed += Time.deltaTime;
            yield return null;
        }

        SetCrowdWorldPositions(
            new Vector3(targetPos[0].x, leftCrowd.transform.position.y, 0),
            new Vector3(targetPos[1].x, rightCrowd.transform.position.y, 0),
            new Vector3(bottomCrowd.transform.position.x, targetPos[2].y, 0),
            new Vector3(topCrowd.transform.position.x, targetPos[3].y, 0)
        );

        canResize=true;

    }
        
        
    public void GetCrowdParents()
    {
        leftCrowd = crowdParentLeft;
        rightCrowd = crowdParentRight;
        bottomCrowd = crowdParentBottom;
        topCrowd = crowdParentTop;

        leftCrowdPos = leftCrowd.transform.position;
        rightCrowdPos = rightCrowd.transform.position;
        bottomCrowdPos = bottomCrowd.transform.position;
        topCrowdPos = topCrowd.transform.position;
    } 

    /*private void SpawnCrowd(BeatTimer beatTimer, int width, int height, float tileSize)
    {
        // Determines how wide and tall the crowd will be
        int xLength = (int)(width * tileSize) / 4;
        int yLength = (int)(height * tileSize) + 5;

        // Spawns columns until the desired width is reached
        for (int i = 0; i < xLength / xOffset; i++)
        {
            // Fills each column with triangles from top to bottom
            for (int j = 0; j < yLength / yOffset; j++)
            {
                float xPos = 0;
                float yPos = 0;
                GameObject triangle = new GameObject();

                // Left Crowd
                xPos = -(i * xOffset) - (tileSize * 0.75f);
                yPos = (j * yOffset) - (tileSize * 0.75f);
                if (i % 2 == 1) yPos += yOffset / 2;
                triangle = Instantiate(trianglePrefab, new Vector3(xPos, yPos, 0), Quaternion.identity);
                triangle.GetComponent<SpriteRenderer>().sortingOrder = (int)(1000 - xPos);
                triangle.transform.SetParent(crowdParentLeft.transform);
                crowd.Add(triangle);

                // Right Crowd
                xPos = (i * xOffset) + ((width - 1) * tileSize) + (tileSize * 0.75f);
                yPos = (j * yOffset) - (tileSize * 0.75f);
                if (i % 2 == 1) yPos += yOffset / 2;
                triangle = Instantiate(trianglePrefab, new Vector3(xPos, yPos, 0), Quaternion.Euler(0, 0, 180));
                triangle.GetComponent<SpriteRenderer>().sortingOrder = (int)(1000 + xPos);
                triangle.transform.SetParent(crowdParentRight.transform);
                crowd.Add(triangle);
            }
        }

        crowdParentBottom.transform.position = new Vector3((width - 1) * tileSize / 2, -10, 0);
        crowdParentTop.transform.position = new Vector3((width - 1) * tileSize / 2, ((height - 1) * tileSize) + 10, 0);

        SelectInitialNodders();

        // Initiaizing each cTriangle
        foreach (GameObject triangle in crowd)
        {
            triangle.GetComponent<cTriangle>().Initialize(beatTimer);
        }
    }*/

}
