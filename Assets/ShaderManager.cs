using UnityEngine;
using System.Collections;
using System.IO;

public class ShaderManager : MonoBehaviour {
	static public int kiCalc;							// kernel index, to reference the exact kernel to run and to set buffers for
	
    // compute buffers are a class to pass information into the compute shader
	static public ComputeBuffer costMatrix;			// this buffer of four double numbers contains rect borders in fractal coordinates
	static public ComputeBuffer globalCostMatrix;			// this buffer of 256 colors contains the colors to paint pixels depending on calculations
    static public ComputeBuffer motionTemplate;          // this buffer of 256 colors contains the colors to paint pixels depending on calculationstic public ComputeBuffer globalCostMatrix;			// this buffer of 256 colors contains the colors to paint pixels depending on calculations
    static public ComputeBuffer motionQuery;            // tmhis buffer of 256 colors contains the colors to paint pixels depending on calculations

    static private int motionTemplateLength = 384;
    static private int motionQueryLength = 16;
    static private int inputDimensions = 1;
    static public ComputeShader _shader;				// we will need to link the code to this class, and then weĺl use it to actually run kernels from the code

    static float[] costMatrixArray = new float[motionTemplateLength * motionQueryLength];
    static float[] globalCostMatrixArray = new float[motionTemplateLength * motionQueryLength];

    static Quaternion[] motionTemplateArray = new Quaternion[motionTemplateLength];
    static Quaternion[] motionQueryArray = new Quaternion[motionQueryLength];

    void Start(){                                       // this runs at start and initializes everything
        initBuffers();                                  // initializes arrays, links them to buffers
        compute();
    }

	static void initBuffers(){

        string s = "";

        for (int i = 0; i < motionQueryLength; i++)
        {
            motionQueryArray[i] = new Quaternion(Mathf.Cos(i*Mathf.PI/(motionQueryLength)),0,0,0);
            s += motionQueryArray[i].ToString() + ",";
        }

        s += "\n";

        for (int i = 0; i < motionTemplateLength; i++)
        {
            motionTemplateArray[i] = new Quaternion(Mathf.Sin(i*Mathf.PI/(motionQueryLength*2)), 0, 0, 0);
          /*  if (i > 10 && i < motionQueryLength*2+10)
                motionTemplateArray[i] = new Quaternion(20, 0, 0, 0);*/
            s += motionTemplateArray[i].ToString() + ",";
        }

        File.WriteAllText("inputQueryAndTemplate.csv", s);

        motionTemplate = new ComputeBuffer(motionTemplateArray.Length, 4 * 4);
        motionTemplate.SetData(motionTemplateArray);

        motionQuery = new ComputeBuffer(motionTemplateArray.Length, 4 * 4);
        motionQuery.SetData(motionQueryArray);

        globalCostMatrix = new ComputeBuffer(globalCostMatrixArray.Length, 4);		
		globalCostMatrix.SetData(globalCostMatrixArray);   

        costMatrix = new ComputeBuffer(costMatrixArray.Length, 4);
        costMatrix.SetData(costMatrixArray);

  	}

    static void compute(){
        _shader = Resources.Load<ComputeShader>("csDTW");			// here we link computer shader code file to the shader class
		kiCalc = _shader.FindKernel("dtwCalc");                      // we retrieve kernel index by name from the code

 		_shader.SetBuffer(kiCalc, "costMatrix", costMatrix);				  
		_shader.SetBuffer(kiCalc, "motionQuery", motionQuery);    
        _shader.SetBuffer(kiCalc, "motionTemplate", motionTemplate);
        _shader.SetBuffer(kiCalc, "globalCostMatrix", globalCostMatrix);
        _shader.Dispatch(kiCalc, 1024, 1, 1);
        globalCostMatrix.GetData(globalCostMatrixArray);
        costMatrix.GetData(costMatrixArray);

        // now, I have a minimum cost of matching each point in in query to entire template as the last element of the array
        // but, if I want to find how the query, starting from the beginning of the template, matches to some place in the template other 
        // than the end, I need to find a minimum value in the last column of the globalCost matrix

        string s = "";
        string ss = "";
        float min = 100000000;
        for (int j = 0; j < motionTemplateLength; j++)
            {
            for (int i = 0; i < motionQueryLength; i++)
                {
                if (i == motionQueryLength - 1 && globalCostMatrixArray[j * motionQueryLength + i] < min)
                {
                     min = globalCostMatrixArray[j * motionQueryLength + i];
                     Debug.Log(min);
                }

                s += globalCostMatrixArray[j * motionQueryLength + i] + ",";

                ss += costMatrixArray[j * motionQueryLength + i] + ",";
            }

            s += "\n";
            ss += "\n";
        }

        File.WriteAllText("resultGlobalCost.csv", s);
        File.WriteAllText("resultCost.csv", ss);
        for (int i = 0; i < 16; i++)
            Debug.Log("Min: "+ (i) +" " + (globalCostMatrixArray[motionQueryLength*motionTemplateLength-1]));
       
        /*
		Note:
		One compute shader file can have different kernels. They can share buffers, but you would have to call SetBuffer() for each kernel
		For example:
		_shader.SetBuffer(kernel1, "someBuffer", bufferToShare);
		_shader.SetBuffer(kernel2, "someBuffer", bufferToShare);
		
		*/
        // Dispatch() method runs its kernel
        /*
			What do the "32, 32, 1" numbers mean? They set the amount of thread groups in X, Y, Z dimensions
			If you open compute shader code of this example, you will see, that our "pixelCalc" kernel has [numthreads(32,32,1)] line,
			and this line means that each group has that much threads per group
			
			The most important thing about these numbers is that kernel has uint3 id : SV_DispatchThreadID parameter
			and this id parameter contains the three dimentional index of the current thread being calculated

			Both Dispatch() and [numthread] have X, Y, Z parameters, and they multiply
			In our case we have (32, 32, 1) groups from Dispatch() and (32, 32, 1) threads per group
			so there will be runned (32 * 32, 32 * 32, 1) = (1024, 1024, 1) threads
			or one thread for each pixel of 1024 x 1024 texture
			we could have Dispatch(kiCalc, 1024, 1024, 1) and [numthreads(1, 1, 1)]
			or Dispatch(kiCalc, 256, 8, 1) and [numthreads(4, 128, 1)]
			it would give us the same (1024, 1024, 1) number of threads
			and inside compute shader we have access to the current thread index through the uint3 id : SV_DispatchThreadID parameter
			In our case we have 1024 threads at X dimension and 1024 threads at Y dimension, so inside compute shader:
			id.x will have values in [0, 1024] range
			id.y will have values in [0, 1024] range
			And therefore we will be able to set the correct pixel's color based on this id parameter

			for more information about thread numbers, check this msdn link:

			https://msdn.microsoft.com/en-us/library/windows/desktop/ff471442(v=vs.85).aspx
			
		*/
    }

    void Update(){
//       compute();
   	}
	
}
