using System;
using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using System.Runtime.InteropServices;
using CommandUndoRedo;
using HoloToolkit.Unity.InputModule;

namespace RuntimeGizmos
{
	//To be safe, if you are changing any transforms hierarchy, such as parenting an object to something,
	//you should call ClearTargets before doing so just to be sure nothing unexpected happens... as well as call UndoRedoManager.Clear()
	//For example, if you select an object that has children, move the children elsewhere, deselect the original object, then try to add those old children to the selection, I think it wont work.

	[RequireComponent(typeof(Camera))]
	public class TransformGizmo : MonoBehaviour
    {
		public TransformSpace space = TransformSpace.Global;
		public TransformType type = TransformType.Move;
		public TransformPivot pivot = TransformPivot.Pivot;
		public CenterType centerType = CenterType.All;
		public ScaleType scaleType = ScaleType.FromPoint;

		//These are the same as the unity editor hotkeys
		public KeyCode SetMoveType = KeyCode.W;
		public KeyCode SetRotateType = KeyCode.E;
		public KeyCode SetScaleType = KeyCode.R;
        public KeyCode SetSpaceToggle = KeyCode.X;
        public KeyCode SetPivotModeToggle = KeyCode.Z;
        public KeyCode SetCenterTypeToggle = KeyCode.C;
        public KeyCode SetScaleTypeToggle = KeyCode.S;
        public KeyCode AddSelection = KeyCode.LeftShift;
		public KeyCode RemoveSelection = KeyCode.LeftControl;
        public KeyCode ActionKey = KeyCode.LeftShift; //Its set to shift instead of control so that while in the editor we dont accidentally undo editor changes =/
        public KeyCode UndoAction = KeyCode.Z;
        public KeyCode RedoAction = KeyCode.Y;

        public Color xColor = new Color(1, 0, 0, 0.8f);
		public Color yColor = new Color(0, 1, 0, 0.8f);
		public Color zColor = new Color(0, 0, 1, 0.8f);
		public Color allColor = new Color(.7f, .7f, .7f, 0.8f);
		public Color selectedColor = new Color(1, 1, 0, 0.8f);
		public Color hoverColor = new Color(1, .75f, 0, 0.8f);

		public float handleLength = .25f;
		public float handleWidth = .003f;
		public float triangleSize = .03f;
		public float boxSize = .03f;
		public int circleDetail = 40;
		public float minSelectedDistanceCheck = .04f;
		public float moveSpeedMultiplier = 1f;
		public float scaleSpeedMultiplier = 1f;
		public float rotateSpeedMultiplier = 200f;
		public float allRotateSpeedMultiplier = 20f;

		public bool useFirstSelectedAsMain = true;

		//Mainly for if you want the pivot point to update correctly if Active objects are moving outside the transformgizmo.
		//Might be poor on performance if lots of objects are Active...
		public bool forceUpdatePivotPointOnChange = true;
        public bool InputDown = false;
        public bool InputUp = true;
        public bool DrawHandles = true;
        public bool DisableGizmo = false;

        public int maxUndoStored = 100;

		AxisVectors handleLines = new AxisVectors();
		AxisVectors handleTriangles = new AxisVectors();
		AxisVectors handleSquares = new AxisVectors();
		AxisVectors circlesLines = new AxisVectors();
		AxisVectors drawCurrentCirclesLines = new AxisVectors();
		
		bool isTransforming;
		float totalScaleAmount;
		Quaternion totalRotationAmount;
		Axis nearAxis = Axis.None;
        public Axis NEAR_AXIS
        {
            get { return nearAxis;}
        }
		AxisInfo axisInfo;

		Vector3 pivotPoint;
        private Vector3 pivotPointSaeved;
		Vector3 totalCenterPivotPoint;
        private Vector3 totalCenterPivotPointSaved;
        private Vector3 manipulationVec;

		//We use a HashSet and a List for targetRoots so that we get fast lookup with the hashset while also keeping track of the order with the list.
		Transform mainTargetRoot {get {return (targetRootsOrdered.Count > 0) ? (useFirstSelectedAsMain) ? targetRootsOrdered[0] : targetRootsOrdered[targetRootsOrdered.Count - 1] : null;}}
		public List<Transform> targetRootsOrdered = new List<Transform>();
        private Vector3[] targetRootsOrderedSavedPos;
		Dictionary<Transform, TargetInfo> targetRoots = new Dictionary<Transform, TargetInfo>();
		HashSet<Renderer> highlightedRenderers = new HashSet<Renderer>();
		HashSet<Transform> children = new HashSet<Transform>();

		List<Transform> childrenBuffer = new List<Transform>();
		List<Renderer> renderersBuffer = new List<Renderer>();
		List<Material> materialsBuffer = new List<Material>();

		Camera myCamera;
		WaitForEndOfFrame waitForEndOFFrame = new WaitForEndOfFrame();
		Coroutine forceUpdatePivotCoroutine;

        // Saved at airtap down.
        private Vector3 _savedScale;
        private Quaternion _savedRotation;

        static Material lineMaterial;
		static Material outlineMaterial;
        

        #region Unity
        void Awake()
		{
			myCamera = GetComponent<Camera>();
			SetMaterial();
		    InputManager.Instance.AddGlobalListener(gameObject);
        }

		void OnEnable()
		{
		    EventManager.ManipulationUpdated += OnManipulationUpdatedLocal;
		    EventManager.ManipulationCompleted += OnManipulationCompletedLocal;
		    EventManager.ManipulationCompleted += OnManipulationCanceledLocal;
            forceUpdatePivotCoroutine = StartCoroutine(ForceUpdatePivotPointAtEndOfFrame());
		}

		void OnDisable()
		{
		    EventManager.ManipulationUpdated -= OnManipulationUpdatedLocal;
		    EventManager.ManipulationCompleted -= OnManipulationCompletedLocal;
		    EventManager.ManipulationCompleted -= OnManipulationCanceledLocal;
            ClearTargets(); //Just so things gets cleaned up, such as removing any materials we placed on objects.

			StopCoroutine(forceUpdatePivotCoroutine);
		}

		void OnDestroy()
		{
			ClearAllHighlightedRenderers();
		}

		void Update()
		{
			HandleUndoRedo();

			SetSpaceAndType();
			SetNearAxis();
		    if (Input.GetKey(KeyCode.T))
		    {
		        nearAxis = Axis.None;
		    }

			if(mainTargetRoot == null) return;
			//print("Vec: " + targetRootsOrdered.Count);
			TransformSelected();
		}

        void LateUpdate()
        {
            if (mainTargetRoot == null) return;
            // If the ContexMenu is on disable the handles.
            // DisableGizmo - way of controling the gizmo display when Grab mode is active.
            if (!ContexMenu.Instance.IsActive && !DisableGizmo)
            {
                //We run this in lateupdate since coroutines run after update and we want our gizmos to have the updated target transform position after TransformSelected()
                SetAxisInfo();
                SetLines();
            }
            else
            {
                handleLines.Clear();
                handleTriangles.Clear();
                handleSquares.Clear();
                circlesLines.Clear();
            }
        }

        void OnPostRender()
		{
			if(mainTargetRoot == null) return;

			lineMaterial.SetPass(0);

			Color xColor = (nearAxis == Axis.X) ? (isTransforming) ? selectedColor : hoverColor : this.xColor;
			Color yColor = (nearAxis == Axis.Y) ? (isTransforming) ? selectedColor : hoverColor : this.yColor;
			Color zColor = (nearAxis == Axis.Z) ? (isTransforming) ? selectedColor : hoverColor : this.zColor;
			Color allColor = (nearAxis == Axis.Any) ? (isTransforming) ? selectedColor : hoverColor : this.allColor;

			//Note: The order of drawing the axis decides what gets drawn over what.

			DrawQuads(handleLines.z, zColor);
			DrawQuads(handleLines.x, xColor);
			DrawQuads(handleLines.y, yColor);

			DrawTriangles(handleTriangles.x, xColor);
			DrawTriangles(handleTriangles.y, yColor);
			DrawTriangles(handleTriangles.z, zColor);

			DrawQuads(handleSquares.x, xColor);
			DrawQuads(handleSquares.y, yColor);
			DrawQuads(handleSquares.z, zColor);
			DrawQuads(handleSquares.all, allColor);

            // When I will use global modifier, work on this part.
			AxisVectors rotationAxisVector = circlesLines;
			if(isTransforming && space == TransformSpace.Global && type == TransformType.Rotate)
			{
				rotationAxisVector = drawCurrentCirclesLines;

				AxisInfo axisInfo = new AxisInfo();
				axisInfo.xDirection = totalRotationAmount * Vector3.right;
				axisInfo.yDirection = totalRotationAmount * Vector3.up;
				axisInfo.zDirection = totalRotationAmount * Vector3.forward;
				SetCircles(axisInfo, drawCurrentCirclesLines);
			}

			DrawQuads(rotationAxisVector.all, allColor);
			DrawQuads(rotationAxisVector.x, xColor);
			DrawQuads(rotationAxisVector.y, yColor);
			DrawQuads(rotationAxisVector.z, zColor);
		}

        public void OnInputDownLocal()
        {
            InputDown = true;
            InputUp = false;
            // Reset manipulation Vector that is responisble for move transformation.
            manipulationVec = Vector3.zero;
            //GetTarget("PREdge", "PRFace");
            // Will run only if this Airtap is not the first one that adds the target.
            if (targetRootsOrdered.Count > 0)
            {
                SavePrevPosition();
                // Save scale of the object, to later only add the manipulation vector.
                _savedScale = targetRootsOrdered[0].localScale;
                _savedRotation = targetRootsOrdered[0].localRotation;
            }

            pivotPointSaeved = pivotPoint;
            totalCenterPivotPointSaved = totalCenterPivotPoint;
        }

        public void OnInputUpLocal()
        {
            InputUp = true;
            InputDown = false;
        }
        private void OnManipulationUpdatedLocal()
        {
            manipulationVec = Manager.Instance.EVENT_MANAGER.EventDataManipulation.CumulativeDelta;
        }

        private void OnManipulationCompletedLocal()
        {
            manipulationVec = Vector3.zero;
        }

        private void OnManipulationCanceledLocal()
        {
            // Reset highlighted handle.
            nearAxis = Axis.None;
        }

        #endregion //Unity

        /// <summary>
        /// Save position of the traget, used to get the difference for maniputations.
        /// </summary>
        public void SavePrevPosition()
        {
            targetRootsOrderedSavedPos = new Vector3[targetRootsOrdered.Count];
            for (int i = 0; i < targetRootsOrdered.Count; i++)
            {
                targetRootsOrderedSavedPos[i] = new Vector3(targetRootsOrdered[i].position.x,
                    targetRootsOrdered[i].position.y,
                    targetRootsOrdered[i].position.z);
            }
        }

        void HandleUndoRedo()
        {
            if (maxUndoStored != UndoRedoManager.maxUndoStored) { UndoRedoManager.maxUndoStored = maxUndoStored; }

            if (Input.GetKey(ActionKey))
            {
                if (Input.GetKeyDown(UndoAction))
                {
                    UndoRedoManager.Undo();
                }
                else if (Input.GetKeyDown(RedoAction))
                {
                    UndoRedoManager.Redo();
                }
            }
        }

        void SetSpaceAndType()
        {
            if (Input.GetKey(ActionKey)) return;

            if (Input.GetKeyDown(SetMoveType)) type = TransformType.Move;
            else if (Input.GetKeyDown(SetRotateType)) type = TransformType.Rotate;
            else if (Input.GetKeyDown(SetScaleType)) type = TransformType.Scale;

            if (Input.GetKeyDown(SetPivotModeToggle))
            {
                if (pivot == TransformPivot.Pivot) pivot = TransformPivot.Center;
                else if (pivot == TransformPivot.Center) pivot = TransformPivot.Pivot;

                SetPivotPoint();
            }

            if (Input.GetKeyDown(SetCenterTypeToggle))
            {
                if (centerType == CenterType.All) centerType = CenterType.Solo;
                else if (centerType == CenterType.Solo) centerType = CenterType.All;

                SetPivotPoint();
            }

            if (Input.GetKeyDown(SetSpaceToggle))
            {
                if (space == TransformSpace.Global) space = TransformSpace.Local;
                else if (space == TransformSpace.Local) space = TransformSpace.Global;
            }

            if (Input.GetKeyDown(SetScaleTypeToggle))
            {
                if (scaleType == ScaleType.FromPoint) scaleType = ScaleType.FromPointOffset;
                else if (scaleType == ScaleType.FromPointOffset) scaleType = ScaleType.FromPoint;
            }

            if (type == TransformType.Scale)
            {
                space = TransformSpace.Local; //Only support local scale
                if (pivot == TransformPivot.Pivot) scaleType = ScaleType.FromPoint; //FromPointOffset can be inaccurate and should only really be used in Center mode if desired.
            }
        }

        void TransformSelected()
		{
			if(mainTargetRoot != null)
			{
				if(nearAxis != Axis.None && InputDown)
				{
					StartCoroutine(TransformSelected(type));
				    InputDown = false;
				}
			}
		}
		
		IEnumerator TransformSelected(TransformType type)
		{
			isTransforming = true;
			totalScaleAmount = 0;
			totalRotationAmount = Quaternion.identity;

			Vector3 originalPivot = pivotPoint;

			Vector3 planeNormal = (transform.position - originalPivot).normalized;
			Vector3 axis = GetNearAxisDirection();
			Vector3 projectedAxis = Vector3.ProjectOnPlane(axis, planeNormal).normalized;
			Vector3 previousMousePosition = Vector3.zero;

			List<ICommand> transformCommands = new List<ICommand>();
			for(int i = 0; i < targetRootsOrdered.Count; i++)
			{
				transformCommands.Add(new TransformCommand(this, targetRootsOrdered[i]));
			}

			while(!InputUp)
			{
			    RaycastHit _hit;
			    Ray _ray = GazeManager.Instance.Rays[0];
				Vector3 mousePosition = Geometry.LinePlaneIntersect(_ray.origin, _ray.direction, originalPivot, planeNormal);
                if (previousMousePosition != Vector3.zero && mousePosition != Vector3.zero)
				{
					if(type == TransformType.Move)
					{
					    float moveAmount = ExtVector3.MagnitudeInDirection(manipulationVec, projectedAxis) * moveSpeedMultiplier;
                        Vector3 movement = axis * moveAmount;
						for(int i = 0; i < targetRootsOrdered.Count; i++)
						{
                            Transform target = targetRootsOrdered[i];
                            // Save target position, in worder to add the movement Vector. Translate gives a continuos transformation.
						    Vector3 targetSavedPos = targetRootsOrderedSavedPos[i];
                            target.position = targetSavedPos + movement;
						}
					    UpdatePivotPositionHL(movement);
					}
					else if(type == TransformType.Scale)
					{
                        // Axix.Any is the whole model scale.
						Vector3 projected = (nearAxis == Axis.Any) ? transform.right : projectedAxis;
                        // Calculate the projected magnitute of the vector on the projectedAxis.
                        float scaleAmount = ExtVector3.MagnitudeInDirection(manipulationVec, projected) * scaleSpeedMultiplier;
                        //WARNING - There is a bug in unity 5.4 and 5.5 that causes InverseTransformDirection to be affected by scale which will break negative scaling. Not tested, but updating to 5.4.2 should fix it - https://issuetracker.unity3d.com/issues/transformdirection-and-inversetransformdirection-operations-are-affected-by-scale
                        Vector3 localAxis = (space == TransformSpace.Local && nearAxis != Axis.Any) ? mainTargetRoot.InverseTransformDirection(axis) : axis;

                        for (int i = 0; i < targetRootsOrdered.Count; i++)
						{
							Transform target = targetRootsOrdered[i];
						    Vector3 targetScale = _savedScale + (localAxis * scaleAmount);

                            if (pivot == TransformPivot.Pivot)
							{
								target.localScale = targetScale;
							}
							else if(pivot == TransformPivot.Center)
							{
								if(scaleType == ScaleType.FromPoint)
								{
                                    target.SetScaleFrom(originalPivot, targetScale);
								}
								else if(scaleType == ScaleType.FromPointOffset)
								{
                                    target.SetScaleFromOffset(originalPivot, targetScale);
								}
							}
						}

						totalScaleAmount = scaleAmount;
					}
					else if(type == TransformType.Rotate)
					{
						float rotateAmount = 0;
						Vector3 rotationAxis = axis;

						if(nearAxis == Axis.Any)
						{
						    Vector3 rotation = transform.TransformDirection(new Vector3(Input.GetAxis("Mouse Y"), -Input.GetAxis("Mouse X"), 0));
                            Quaternion.Euler(rotation).ToAngleAxis(out rotateAmount, out rotationAxis);
							rotateAmount *= allRotateSpeedMultiplier;
						}else
						{
							Vector3 projected = (nearAxis == Axis.Any || ExtVector3.IsParallel(axis, planeNormal)) ? planeNormal : Vector3.Cross(axis, planeNormal);
							//rotateAmount = (ExtVector3.MagnitudeInDirection(mousePosition - previousMousePosition, projected) * rotateSpeedMultiplier) / GetDistanceMultiplier();
						    rotateAmount = (ExtVector3.MagnitudeInDirection(manipulationVec, projected) * rotateSpeedMultiplier);
						    Debug.Log("Projected: " + GetDistanceMultiplier());
                            Debug.Log("rotateAmount: " + rotateAmount);
						    Debug.Log("rotateAxis: " + rotationAxis);
                        }
                        // Soleve the problem with continuous rotation.
					    for (int i = 0; i < targetRootsOrdered.Count; i++)
					    {
					        Transform target = targetRootsOrdered[i];
					        if (pivot == TransformPivot.Pivot)
					        {
					            if (nearAxis == Axis.X)
					            {
					                // Add the change of angle to the saved angle on X
					                float xAmount = _savedRotation.eulerAngles.x + rotateAmount;
					                target.localRotation = Quaternion.Euler(xAmount, _savedRotation.eulerAngles.y,
					                    _savedRotation.eulerAngles.z);
					            }
					            else if (nearAxis == Axis.Y)
					            {
					                // Add the change of angle to the saved angle on Y
					                float xAmount = _savedRotation.eulerAngles.y + rotateAmount;
					                target.localRotation = Quaternion.Euler(_savedRotation.eulerAngles.x, xAmount,
					                    _savedRotation.eulerAngles.z);
					            }
					            else if (nearAxis == Axis.Z)
					            {
					                // Add the change of angle to the saved angle on Z
					                float xAmount = _savedRotation.eulerAngles.z + rotateAmount;
					                target.localRotation = Quaternion.Euler(_savedRotation.eulerAngles.x,
					                    _savedRotation.eulerAngles.y, xAmount);
					            }
					        }
                            // I don't use this.
					        else if (pivot == TransformPivot.Center)
					        {
					            target.RotateAround(originalPivot, rotationAxis, rotateAmount);
					        }
					        
                        }

					    totalRotationAmount *= Quaternion.Euler(rotationAxis * rotateAmount);
                    }
				}

				previousMousePosition = mousePosition;

				yield return null;
			}

			for(int i = 0; i < transformCommands.Count; i++)
			{
				((TransformCommand)transformCommands[i]).StoreNewTransformValues();
			}
			CommandGroup commandGroup = new CommandGroup();
			commandGroup.Set(transformCommands);
			UndoRedoManager.Insert(commandGroup);

			totalRotationAmount = Quaternion.identity;
			totalScaleAmount = 0;
			isTransforming = false;

			SetPivotPoint();
		}

		Vector3 GetNearAxisDirection()
		{
			if(nearAxis != Axis.None)
			{
				if(nearAxis == Axis.X) return axisInfo.xDirection;
				if(nearAxis == Axis.Y) return axisInfo.yDirection;
				if(nearAxis == Axis.Z) return axisInfo.zDirection;
				if(nearAxis == Axis.Any) return Vector3.one;
			}
			return Vector3.zero;
		}
        
        /// <summary>
        /// Modified vertion of GetTarget to work with specific tag1 or object.
        /// </summary>
        /// <param name="tag1">Tag name of the targeted object.</param>
        void GetTarget(string tag1, string tag2)
        {
            if (nearAxis == Axis.None)
            {
                //bool isAdding = Input.GetKey(AddSelection);
                //bool isRemoving = Input.GetKey(RemoveSelection);
                bool isAdding = false;
                bool isRemoving = false;

                RaycastHit hit;
                Ray ray = GazeManager.Instance.Rays[0];

                if (Physics.Raycast(ray, out hit))
                {
                    Transform target = hit.transform;
                    if (isAdding)
                    {
                        //AddTarget(target);
                    }
                    else if (isRemoving)
                    {
                        //RemoveTarget(target);
                    }
                    else if (!isAdding && !isRemoving)
                    {
                        
                        if (hit.collider.tag == tag1 || hit.collider.tag == tag2)
                        {
                            ClearAndAddTarget(target);
                            return;
                        }
                        ClearTargets();
                    }
                }
                else
                {
                    if (!isAdding && !isRemoving)
                    {
                        ClearTargets();
                    }
                }
            }
        }

        void GetTarget(Ray ray)
		{
			if(nearAxis == Axis.None)
			{
				bool isAdding = Input.GetKey(AddSelection);
				bool isRemoving = Input.GetKey(RemoveSelection);

				RaycastHit hitInfo; 
				if(Physics.Raycast(ray, out hitInfo))
				{
					Transform target = hitInfo.transform;

					if(isAdding)
					{
						AddTarget(target);
					}
					else if(isRemoving)
					{
						RemoveTarget(target);
					}
					else if(!isAdding && !isRemoving)
					{
						ClearAndAddTarget(target);
					}
				}else{
					if(!isAdding && !isRemoving)
					{
						ClearTargets();
					}
				}
			}
		}

		public void AddTarget(Transform target, bool addCommand = true)
		{
			if(target != null)
			{
				if(targetRoots.ContainsKey(target)) return;
				if(children.Contains(target)) return;

				if(addCommand) UndoRedoManager.Insert(new AddTargetCommand(this, target, targetRootsOrdered));

				AddTargetRoot(target);
			    //AddTargetHighlightedRenderers(target);

				SetPivotPoint();
			}
		}

		public void RemoveTarget(Transform target, bool addCommand = true)
		{
			if(target != null)
			{
				if(!targetRoots.ContainsKey(target)) return;

				if(addCommand) UndoRedoManager.Insert(new RemoveTargetCommand(this, target));

				RemoveTargetHighlightedRenderers(target);
				RemoveTargetRoot(target);

				SetPivotPoint();
			}
		}

		public void ClearTargets(bool addCommand = true)
		{
			if(addCommand) UndoRedoManager.Insert(new ClearTargetsCommand(this, targetRootsOrdered));

			ClearAllHighlightedRenderers();
			targetRoots.Clear();
			targetRootsOrdered.Clear();
			children.Clear();
		}

		public void ClearAndAddTarget(Transform target)
		{
			UndoRedoManager.Insert(new ClearAndAddTargetCommand(this, target, targetRootsOrdered));

			ClearTargets(false);
			AddTarget(target, false);
		}

        void AddTargetHighlightedRenderers(Transform target)
        {
            if (target != null)
            {
                GetTargetRenderers(target, renderersBuffer);

                for (int i = 0; i < renderersBuffer.Count; i++)
                {
                    Renderer render = renderersBuffer[i];

                    if (!highlightedRenderers.Contains(render))
                    {
                        materialsBuffer.Clear();
                        materialsBuffer.AddRange(render.sharedMaterials);

                        if (!materialsBuffer.Contains(outlineMaterial))
                        {
                            materialsBuffer.Add(outlineMaterial);
                            render.materials = materialsBuffer.ToArray();
                        }

                        highlightedRenderers.Add(render);
                    }
                }

                materialsBuffer.Clear();
            }
        }

        void GetTargetRenderers(Transform target, List<Renderer> renderers)
		{
			renderers.Clear();
			if(target != null)
			{
				target.GetComponentsInChildren<Renderer>(true, renderers);
			}
		}

		void ClearAllHighlightedRenderers()
		{
			foreach(var target in targetRoots)
			{
				RemoveTargetHighlightedRenderers(target.Key);
			}

			//In case any are still left, such as if they changed parents or what not when they were highlighted.
			renderersBuffer.Clear();
			renderersBuffer.AddRange(highlightedRenderers);
			RemoveHighlightedRenderers(renderersBuffer);
		}

		void RemoveTargetHighlightedRenderers(Transform target)
		{
			GetTargetRenderers(target, renderersBuffer);

		    RemoveHighlightedRenderersHL(renderersBuffer);
		}

        void RemoveHighlightedRenderersHL(List<Renderer> renderers)
        {
            for (int i = 0; i < renderersBuffer.Count; i++)
            {
                Renderer render = renderersBuffer[i];
                if (render != null)
                {
                    materialsBuffer.Clear();
                    materialsBuffer.AddRange(render.sharedMaterials);

                    if (materialsBuffer.Contains(outlineMaterial))
                    {
                        materialsBuffer.Remove(outlineMaterial);
                        render.materials = materialsBuffer.ToArray();
                    }
                }

                highlightedRenderers.Remove(render);
            }

            renderersBuffer.Clear();
        }

        void RemoveHighlightedRenderers(List<Renderer> renderers)
		{
			for(int i = 0; i < renderersBuffer.Count; i++)
			{
				Renderer render = renderersBuffer[i];
				if(render != null)
				{
					materialsBuffer.Clear();
					materialsBuffer.AddRange(render.sharedMaterials);

					if(materialsBuffer.Contains(outlineMaterial))
					{
						materialsBuffer.Remove(outlineMaterial);
						render.materials = materialsBuffer.ToArray();
					}
				}

				highlightedRenderers.Remove(render);
			}

			renderersBuffer.Clear();
		}

		void AddTargetRoot(Transform targetRoot)
		{
			targetRoots.Add(targetRoot, new TargetInfo());
			targetRootsOrdered.Add(targetRoot);

			AddAllChildren(targetRoot);
		}
		void RemoveTargetRoot(Transform targetRoot)
		{
			if(targetRoots.Remove(targetRoot))
			{
				targetRootsOrdered.Remove(targetRoot);

				RemoveAllChildren(targetRoot);
			}
		}

		void AddAllChildren(Transform target)
		{
			childrenBuffer.Clear();
			target.GetComponentsInChildren<Transform>(true, childrenBuffer);
			childrenBuffer.Remove(target);

			for(int i = 0; i < childrenBuffer.Count; i++)
			{
				Transform child = childrenBuffer[i];
				children.Add(child);
				RemoveTargetRoot(child); //We do this in case we Active child first and then the parent.
			}

			childrenBuffer.Clear();
		}
		void RemoveAllChildren(Transform target)
		{
			childrenBuffer.Clear();
			target.GetComponentsInChildren<Transform>(true, childrenBuffer);
			childrenBuffer.Remove(target);

			for(int i = 0; i < childrenBuffer.Count; i++)
			{
				children.Remove(childrenBuffer[i]);
			}

			childrenBuffer.Clear();
		}

		public void SetPivotPoint()
		{
			if(mainTargetRoot != null)
			{
				if(pivot == TransformPivot.Pivot)
				{
					pivotPoint = mainTargetRoot.position;
				}
				else if(pivot == TransformPivot.Center)
				{
					totalCenterPivotPoint = Vector3.zero;

					Dictionary<Transform, TargetInfo>.Enumerator targetsEnumerator = targetRoots.GetEnumerator(); //We avoid foreach to avoid garbage.
					while(targetsEnumerator.MoveNext())
					{
						Transform target = targetsEnumerator.Current.Key;
						TargetInfo info = targetsEnumerator.Current.Value;
						info.centerPivotPoint = target.GetCenter(centerType);

						totalCenterPivotPoint += info.centerPivotPoint;
					}

					totalCenterPivotPoint /= targetRoots.Count;

					if(centerType == CenterType.Solo)
					{
						pivotPoint = targetRoots[mainTargetRoot].centerPivotPoint;
					}
					else if(centerType == CenterType.All)
					{
						pivotPoint = totalCenterPivotPoint;
					}
				}
			}
		}
		void SetPivotPointOffset(Vector3 offset)
		{
			pivotPoint += offset;
			totalCenterPivotPoint += offset;
		}

        void UpdatePivotPositionHL(Vector3 movement)
        {
            pivotPoint = pivotPointSaeved + movement;
            totalCenterPivotPoint = totalCenterPivotPointSaved + movement;
        }


		IEnumerator ForceUpdatePivotPointAtEndOfFrame()
		{
			while(this.enabled)
			{
				ForceUpdatePivotPointOnChange();
				yield return waitForEndOFFrame;
			}
		}

		void ForceUpdatePivotPointOnChange()
		{
			if(forceUpdatePivotPointOnChange)
			{
				if(mainTargetRoot != null && !isTransforming)
				{
					bool hasSet = false;
					Dictionary<Transform, TargetInfo>.Enumerator targets = targetRoots.GetEnumerator();
					while(targets.MoveNext())
					{
						if(!hasSet)
						{
							if(targets.Current.Value.previousPosition != Vector3.zero && targets.Current.Key.position != targets.Current.Value.previousPosition)
							{
								SetPivotPoint();
								hasSet = true;
							}
						}

						targets.Current.Value.previousPosition = targets.Current.Key.position;
					}
				}
			}
		}

		AxisVectors axisVectorsBuffer = new AxisVectors();
		void SetNearAxis()
		{
			if(isTransforming) return;

			nearAxis = Axis.None;

			if(mainTargetRoot == null) return;

			float distanceMultiplier = GetDistanceMultiplier();
			float handleMinSelectedDistanceCheck = (this.minSelectedDistanceCheck + handleWidth) * distanceMultiplier;

			if(type == TransformType.Move || type == TransformType.Scale)
			{
				float tipMinSelectedDistanceCheck = 0;
				axisVectorsBuffer.Clear();
				
				if(type == TransformType.Move)
				{
					tipMinSelectedDistanceCheck = (this.minSelectedDistanceCheck + triangleSize) * distanceMultiplier;
					axisVectorsBuffer.Add(handleTriangles);
				}
				else if(type == TransformType.Scale)
				{
					tipMinSelectedDistanceCheck = (this.minSelectedDistanceCheck + boxSize) * distanceMultiplier;
					axisVectorsBuffer.Add(handleSquares);
				}

				HandleNearest(axisVectorsBuffer, tipMinSelectedDistanceCheck);

				if(nearAxis == Axis.None)
				{
					HandleNearest(handleLines, handleMinSelectedDistanceCheck);
				}
			}
			else if(type == TransformType.Rotate)
			{
				HandleNearest(circlesLines, handleMinSelectedDistanceCheck);
			}
		}

		void HandleNearest(AxisVectors axisVectors, float minSelectedDistanceCheck)
		{
			float xClosestDistance = ClosestDistanceFromMouseToLines(axisVectors.x);
			float yClosestDistance = ClosestDistanceFromMouseToLines(axisVectors.y);
			float zClosestDistance = ClosestDistanceFromMouseToLines(axisVectors.z);
			float allClosestDistance = ClosestDistanceFromMouseToLines(axisVectors.all);

			if(type == TransformType.Scale && allClosestDistance <= minSelectedDistanceCheck) nearAxis = Axis.Any;
			else if(xClosestDistance <= minSelectedDistanceCheck && xClosestDistance <= yClosestDistance && xClosestDistance <= zClosestDistance) nearAxis = Axis.X;
			else if(yClosestDistance <= minSelectedDistanceCheck && yClosestDistance <= xClosestDistance && yClosestDistance <= zClosestDistance) nearAxis = Axis.Y;
			else if(zClosestDistance <= minSelectedDistanceCheck && zClosestDistance <= xClosestDistance && zClosestDistance <= yClosestDistance) nearAxis = Axis.Z;
			else if(type == TransformType.Rotate && mainTargetRoot != null)
			{
				Ray mouseRay = myCamera.ScreenPointToRay(Input.mousePosition);
				Vector3 mousePlaneHit = Geometry.LinePlaneIntersect(mouseRay.origin, mouseRay.direction, pivotPoint, (transform.position - pivotPoint).normalized);
				if((pivotPoint - mousePlaneHit).sqrMagnitude <= (handleLength * GetDistanceMultiplier()).Squared()) nearAxis = Axis.Any;
			}
		}

		float ClosestDistanceFromMouseToLines(List<Vector3> lines)
		{
			//Ray mouseRay = myCamera.ScreenPointToRay(Input.mousePosition);
		    Ray mouseRay = GazeManager.Instance.Rays[0];

            float closestDistance = float.MaxValue;
			for(int i = 0; i < lines.Count; i += 2)
			{
				IntersectPoints points = Geometry.ClosestPointsOnSegmentToLine(lines[i], lines[i + 1], mouseRay.origin, mouseRay.direction);
				float distance = Vector3.Distance(points.first, points.second);
				if(distance < closestDistance)
				{
					closestDistance = distance;
				}
			}
			return closestDistance;
		}

		void SetAxisInfo()
		{
			if(mainTargetRoot != null)
			{
				float size = handleLength * GetDistanceMultiplier();
				axisInfo.Set(mainTargetRoot, pivotPoint, size, space);

				if(isTransforming && type == TransformType.Scale)
				{
					if(nearAxis == Axis.Any) axisInfo.Set(mainTargetRoot, pivotPoint, size + totalScaleAmount, space);
					if(nearAxis == Axis.X) axisInfo.xAxisEnd += (axisInfo.xDirection * totalScaleAmount);
					if(nearAxis == Axis.Y) axisInfo.yAxisEnd += (axisInfo.yDirection * totalScaleAmount);
					if(nearAxis == Axis.Z) axisInfo.zAxisEnd += (axisInfo.zDirection * totalScaleAmount);
                }
			}
		}

		//This helps keep the size consistent no matter how far we are from it.
		float GetDistanceMultiplier()
		{
			if(mainTargetRoot == null) return 0f;

			if(myCamera.orthographic) return Mathf.Max(.01f, myCamera.orthographicSize * 2f);
			return Mathf.Max(.01f, Mathf.Abs(ExtVector3.MagnitudeInDirection(pivotPoint - transform.position, myCamera.transform.forward)));
		}

		void SetLines()
		{
			SetHandleLines();
			SetHandleTriangles();
			SetHandleSquares();
			SetCircles(axisInfo, circlesLines);
		}

		void SetHandleLines()
		{
			handleLines.Clear();

			if(type == TransformType.Move || type == TransformType.Scale)
			{
				float distanceMultiplier = GetDistanceMultiplier();
				float lineWidth = handleWidth * distanceMultiplier;
				//When scaling, the axis will have different line lengths and direction.
				float xLineLength = Vector3.Distance(pivotPoint, axisInfo.xAxisEnd) * AxisDirectionMultiplier(axisInfo.xAxisEnd - pivotPoint, axisInfo.xDirection);
				float yLineLength = Vector3.Distance(pivotPoint, axisInfo.yAxisEnd) * AxisDirectionMultiplier(axisInfo.yAxisEnd - pivotPoint, axisInfo.yDirection);
				float zLineLength = Vector3.Distance(pivotPoint, axisInfo.zAxisEnd) * AxisDirectionMultiplier(axisInfo.zAxisEnd - pivotPoint, axisInfo.zDirection);

				AddQuads(pivotPoint, axisInfo.xDirection, axisInfo.yDirection, axisInfo.zDirection, xLineLength, lineWidth, handleLines.x);
				AddQuads(pivotPoint, axisInfo.yDirection, axisInfo.xDirection, axisInfo.zDirection, yLineLength, lineWidth, handleLines.y);
				AddQuads(pivotPoint, axisInfo.zDirection, axisInfo.xDirection, axisInfo.yDirection, zLineLength, lineWidth, handleLines.z);
			}
		}
		int AxisDirectionMultiplier(Vector3 direction, Vector3 otherDirection)
		{
			return ExtVector3.IsInDirection(direction, otherDirection) ? 1 : -1;
		}

		void SetHandleTriangles()
		{
			handleTriangles.Clear();

			if(type == TransformType.Move)
			{
				float triangleLength = triangleSize * GetDistanceMultiplier();
				AddTriangles(axisInfo.xAxisEnd, axisInfo.xDirection, axisInfo.yDirection, axisInfo.zDirection, triangleLength, handleTriangles.x);
				AddTriangles(axisInfo.yAxisEnd, axisInfo.yDirection, axisInfo.xDirection, axisInfo.zDirection, triangleLength, handleTriangles.y);
				AddTriangles(axisInfo.zAxisEnd, axisInfo.zDirection, axisInfo.yDirection, axisInfo.xDirection, triangleLength, handleTriangles.z);
			}
		}

		void AddTriangles(Vector3 axisEnd, Vector3 axisDirection, Vector3 axisOtherDirection1, Vector3 axisOtherDirection2, float size, List<Vector3> resultsBuffer)
		{
			Vector3 endPoint = axisEnd + (axisDirection * (size * 2f));
			Square baseSquare = GetBaseSquare(axisEnd, axisOtherDirection1, axisOtherDirection2, size / 2f);

			resultsBuffer.Add(baseSquare.bottomLeft);
			resultsBuffer.Add(baseSquare.topLeft);
			resultsBuffer.Add(baseSquare.topRight);
			resultsBuffer.Add(baseSquare.topLeft);
			resultsBuffer.Add(baseSquare.bottomRight);
			resultsBuffer.Add(baseSquare.topRight);

			for(int i = 0; i < 4; i++)
			{
				resultsBuffer.Add(baseSquare[i]);
				resultsBuffer.Add(baseSquare[i + 1]);
				resultsBuffer.Add(endPoint);
			}
		}

		void SetHandleSquares()
		{
			handleSquares.Clear();

			if(type == TransformType.Scale)
			{
				float boxSize = this.boxSize * GetDistanceMultiplier();
				AddSquares(axisInfo.xAxisEnd, axisInfo.xDirection, axisInfo.yDirection, axisInfo.zDirection, boxSize, handleSquares.x);
				AddSquares(axisInfo.yAxisEnd, axisInfo.yDirection, axisInfo.xDirection, axisInfo.zDirection, boxSize, handleSquares.y);
				AddSquares(axisInfo.zAxisEnd, axisInfo.zDirection, axisInfo.xDirection, axisInfo.yDirection, boxSize, handleSquares.z);
				AddSquares(pivotPoint - (axisInfo.xDirection * (boxSize * .5f)), axisInfo.xDirection, axisInfo.yDirection, axisInfo.zDirection, boxSize, handleSquares.all);
			}
		}

		void AddSquares(Vector3 axisStart, Vector3 axisDirection, Vector3 axisOtherDirection1, Vector3 axisOtherDirection2, float size, List<Vector3> resultsBuffer)
		{
			AddQuads(axisStart, axisDirection, axisOtherDirection1, axisOtherDirection2, size, size * .5f, resultsBuffer);
		}
		void AddQuads(Vector3 axisStart, Vector3 axisDirection, Vector3 axisOtherDirection1, Vector3 axisOtherDirection2, float length, float width, List<Vector3> resultsBuffer)
		{
			Vector3 axisEnd = axisStart + (axisDirection * length);
			AddQuads(axisStart, axisEnd, axisOtherDirection1, axisOtherDirection2, width, resultsBuffer);
		}
		void AddQuads(Vector3 axisStart, Vector3 axisEnd, Vector3 axisOtherDirection1, Vector3 axisOtherDirection2, float width, List<Vector3> resultsBuffer)
		{
			Square baseRectangle = GetBaseSquare(axisStart, axisOtherDirection1, axisOtherDirection2, width);
			Square baseRectangleEnd = GetBaseSquare(axisEnd, axisOtherDirection1, axisOtherDirection2, width);

			resultsBuffer.Add(baseRectangle.bottomLeft);
			resultsBuffer.Add(baseRectangle.topLeft);
			resultsBuffer.Add(baseRectangle.topRight);
			resultsBuffer.Add(baseRectangle.bottomRight);

			resultsBuffer.Add(baseRectangleEnd.bottomLeft);
			resultsBuffer.Add(baseRectangleEnd.topLeft);
			resultsBuffer.Add(baseRectangleEnd.topRight);
			resultsBuffer.Add(baseRectangleEnd.bottomRight);

			for(int i = 0; i < 4; i++)
			{
				resultsBuffer.Add(baseRectangle[i]);
				resultsBuffer.Add(baseRectangleEnd[i]);
				resultsBuffer.Add(baseRectangleEnd[i + 1]);
				resultsBuffer.Add(baseRectangle[i + 1]);
			}
		}

		Square GetBaseSquare(Vector3 axisEnd, Vector3 axisOtherDirection1, Vector3 axisOtherDirection2, float size)
		{
			Square square;
			Vector3 offsetUp = ((axisOtherDirection1 * size) + (axisOtherDirection2 * size));
			Vector3 offsetDown = ((axisOtherDirection1 * size) - (axisOtherDirection2 * size));
			//These might not really be the proper directions, as in the bottomLeft might not really be at the bottom left...
			square.bottomLeft = axisEnd + offsetDown;
			square.topLeft = axisEnd + offsetUp;
			square.bottomRight = axisEnd - offsetUp;
			square.topRight = axisEnd - offsetDown;
			return square;
		}

		void SetCircles(AxisInfo axisInfo, AxisVectors axisVectors)
		{
			axisVectors.Clear();

			if(type == TransformType.Rotate)
			{
				float circleLength = handleLength * GetDistanceMultiplier();
				AddCircle(pivotPoint, axisInfo.xDirection, circleLength, axisVectors.x);
				AddCircle(pivotPoint, axisInfo.yDirection, circleLength, axisVectors.y);
				AddCircle(pivotPoint, axisInfo.zDirection, circleLength, axisVectors.z);
				AddCircle(pivotPoint, (pivotPoint - transform.position).normalized, circleLength, axisVectors.all, false);
			}
		}

		void AddCircle(Vector3 origin, Vector3 axisDirection, float size, List<Vector3> resultsBuffer, bool depthTest = true)
		{
			Vector3 up = axisDirection.normalized * size;
			Vector3 forward = Vector3.Slerp(up, -up, .5f);
			Vector3 right = Vector3.Cross(up, forward).normalized * size;
		
			Matrix4x4 matrix = new Matrix4x4();
		
			matrix[0] = right.x;
			matrix[1] = right.y;
			matrix[2] = right.z;
		
			matrix[4] = up.x;
			matrix[5] = up.y;
			matrix[6] = up.z;
		
			matrix[8] = forward.x;
			matrix[9] = forward.y;
			matrix[10] = forward.z;
		
			Vector3 lastPoint = origin + matrix.MultiplyPoint3x4(new Vector3(Mathf.Cos(0), 0, Mathf.Sin(0)));
			Vector3 nextPoint = Vector3.zero;
			float multiplier = 360f / circleDetail;

			Plane plane = new Plane((transform.position - pivotPoint).normalized, pivotPoint);

			float circleHandleWidth = handleWidth * GetDistanceMultiplier();

			for(int i = 0; i < circleDetail + 1; i++)
			{
				nextPoint.x = Mathf.Cos((i * multiplier) * Mathf.Deg2Rad);
				nextPoint.z = Mathf.Sin((i * multiplier) * Mathf.Deg2Rad);
				nextPoint.y = 0;
			
				nextPoint = origin + matrix.MultiplyPoint3x4(nextPoint);
			
				if(!depthTest || plane.GetSide(lastPoint))
				{
					Vector3 centerPoint = (lastPoint + nextPoint) * .5f;
					Vector3 upDirection = (centerPoint - origin).normalized;
					AddQuads(lastPoint, nextPoint, upDirection, axisDirection, circleHandleWidth, resultsBuffer);
				}

				lastPoint = nextPoint;
			}
		}

		void DrawLines(List<Vector3> lines, Color color)
		{
			GL.Begin(GL.LINES);
			GL.Color(color);

			for(int i = 0; i < lines.Count; i += 2)
			{
				GL.Vertex(lines[i]);
				GL.Vertex(lines[i + 1]);
			}

			GL.End();
		}

		void DrawTriangles(List<Vector3> lines, Color color)
		{
			GL.Begin(GL.TRIANGLES);
			GL.Color(color);

			for(int i = 0; i < lines.Count; i += 3)
			{
				GL.Vertex(lines[i]);
				GL.Vertex(lines[i + 1]);
				GL.Vertex(lines[i + 2]);
			}

			GL.End();
		}

		void DrawQuads(List<Vector3> lines, Color color)
		{
			GL.Begin(GL.QUADS);
			GL.Color(color);

			for(int i = 0; i < lines.Count; i += 4)
			{
				GL.Vertex(lines[i]);
				GL.Vertex(lines[i + 1]);
				GL.Vertex(lines[i + 2]);
				GL.Vertex(lines[i + 3]);
			}

			GL.End();
		}

		void SetMaterial()
		{
			if(lineMaterial == null)
			{
				lineMaterial = new Material(Shader.Find("Custom/Lines"));
				//outlineMaterial = new Material(Shader.Find("Custom/Outline"));
			}
		}

    }
}
