using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Portal : MonoBehaviour
{
    [Header("Main Settings")]
    public Portal linkedPortal; // 链接的另一个传送门
    public MeshRenderer screen; // 传送门的显示屏幕

    [Tooltip("开启后允许传送门嵌套显示（消耗较大）。关闭后仅渲染一层，不再分配临时缓冲纹理。")]
    public bool AllowNestingPortal = true; // 新增开关

    [Tooltip("递归渲染限制。禁用双缓冲时强制为1。")]
    public int recursionLimit = 5; // 递归渲染限制，决定你能通过传送门看到多少层“门中门”

    [Header("Advanced Settings")]
    public float nearClipOffset = 0.05f; // 斜向裁剪面的偏移量，防止产生 Z-fighting
    public float nearClipLimit = 0.2f;   // 裁剪平面安全阈值，相机离门太近时，禁用斜向裁剪，避免严重画面畸变。


    // 私有变量
    RenderTexture viewTexture;
    RenderTexture tempTexture; // 双缓冲，以在 URP 中支持嵌套显示
    Camera portalCam; // 传送门专用摄像机
    Camera playerCam; // 玩家摄像机
    Material firstRecursionMat; // 递归时，优化第一层门的渲染效果
    List<PortalTraveller> trackedTravellers; // 列表，记录所有正在穿过门的物体
    MeshFilter screenMeshFilter; // 传送门边界，在递归渲染时用于剔除

    // 初始化：相机、禁用传送门相机自动渲染、物体列表
    // 自带函数，被唤醒的第一时间执行
    void Awake()
    {
        playerCam = Camera.main;
        if (playerCam == null)
        {
            Debug.LogError($"[传送门错误] {gameObject.name} 找不到主摄像机！请检查场景中是否有 Tag 为 'MainCamera' 的摄像机。组件已自动禁用。");
            enabled = false;
            return;
        }

        portalCam = GetComponentInChildren<Camera>();
        if (portalCam == null)
        {
            Debug.LogError($"[传送门错误] {gameObject.name} 缺少子摄像机组件！组件已自动禁用。");
            enabled = false;
            return;
        }

        // 主动销毁子相机上多余的音频监听器，解决 Audio Listener 警告
        AudioListener listener = portalCam.GetComponent<AudioListener>();
        if (listener != null)
        {
            Destroy(listener);
        }

        if (screen == null)
        {
            Debug.LogError($"[传送门错误] {gameObject.name} 的 Screen 未赋值！组件已自动禁用。");
            enabled = false;
            return;
        }

        portalCam.enabled = false; // 禁用摄像机，将通过脚本手动调用 Render()
        trackedTravellers = new List<PortalTraveller>();
        screenMeshFilter = screen.GetComponent<MeshFilter>();


        // 设置初始遮罩，用于处理递归渲染时的显示层级
        screen.material.SetInt("displayMask", 1);
    }

    // 在 Start 阶段检查外部依赖（确保所有对象的 Awake 已执行完毕）
    void Start()
    {
        if (linkedPortal == null || linkedPortal.screen == null)
        {
            Debug.LogError($"[传送门错误] {gameObject.name} 未关联目标 Portal，或目标 Portal 配置不完整！该传送门将被跳过渲染。");
            enabled = false;
        }
    }

    // 更新正在穿过门的物体（只调用HandleTravellers()）
    void LateUpdate()
    {
        if (!enabled) return;
        HandleTravellers();
    }

    // 在本帧任何传送门摄像机开始渲染之前调用
    public void PrePortalRender()
    {
        if (!enabled) return; // 拦截未正确初始化的组件
        foreach (var traveller in trackedTravellers)
        {
            UpdateSliceParams(traveller);
        }
    }

    // 手动渲染此传送门挂载的摄像机
    // 调用顺序在 PrePortalRender 之后，PostPortalRender 之前
    public void Render(ScriptableRenderContext context)
    {
        if (!enabled) return; // 拦截未正确初始化的组件

        if (!CameraUtility.VisibleFromCamera(linkedPortal.screen, playerCam)) return;

        CreateViewTexture();

        var localToWorldMatrix = playerCam.transform.localToWorldMatrix;
        var renderPositions = new Vector3[recursionLimit];
        var renderRotations = new Quaternion[recursionLimit];

        int startIndex = 0;
        portalCam.projectionMatrix = playerCam.projectionMatrix;

        for (int i = 0; i < recursionLimit; i++)
        {
            if (i > 0)
            {
                if (!CameraUtility.BoundsOverlap(screenMeshFilter, linkedPortal.screenMeshFilter, portalCam)) break;
            }
            localToWorldMatrix = transform.localToWorldMatrix * linkedPortal.transform.worldToLocalMatrix * localToWorldMatrix;
            int renderOrderIndex = recursionLimit - i - 1;
            renderPositions[renderOrderIndex] = localToWorldMatrix.GetColumn(3);
            renderRotations[renderOrderIndex] = localToWorldMatrix.rotation;

            portalCam.transform.SetPositionAndRotation(renderPositions[renderOrderIndex], renderRotations[renderOrderIndex]);
            startIndex = renderOrderIndex;
        }

        screen.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;

        // 初始化一个 PropertyBlock，并在循环前隐藏传送门
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        linkedPortal.screen.GetPropertyBlock(block);
        block.SetInt("displayMask", 0);
        linkedPortal.screen.SetPropertyBlock(block);

        for (int i = startIndex; i < recursionLimit; i++)
        {
            portalCam.transform.SetPositionAndRotation(renderPositions[i], renderRotations[i]);
            SetNearClipPlane();
            HandleClipping();

            // --- 核心双缓冲逻辑 ---
            // 每一层渲染，我们交替使用 viewTexture 和 tempTexture
            // 确保“正在画的”和“正在看的”不是同一个纹理
            // 彻底移除 Graphics.Blit
            int passesRemaining = recursionLimit - i;
            bool renderToViewTex = (passesRemaining % 2 != 0);

            RenderTexture currentTarget = renderToViewTex ? viewTexture : tempTexture;
            RenderTexture previousResult = renderToViewTex ? tempTexture : viewTexture;

            portalCam.targetTexture = currentTarget;

            // 让当前镜头看到的门，显示上一层渲染出的画面
            if (i > startIndex)
            {
                // 使用 MaterialPropertyBlock 强制绕过材质缓存，立即更新纹理
                linkedPortal.screen.GetPropertyBlock(block);

                // 潜在排查提示：如果在极少数情况下仍然无效，且你的 Shader 是 URP Lit/Unlit，
                // 这里的 "_MainTex" 可能需要改为 "_BaseMap"。但在你目前的状况下 _MainTex 应该没问题。
                block.SetTexture("_MainTex", previousResult);
                block.SetInt("displayMask", 1);

                linkedPortal.screen.SetPropertyBlock(block);
            }

            // 执行 URP 渲染
            UniversalRenderPipeline.RenderSingleCamera(context, portalCam);
        }

        // 最后阶段同样使用 MaterialPropertyBlock
        linkedPortal.screen.GetPropertyBlock(block);
        block.SetTexture("_MainTex", viewTexture);
        block.SetInt("displayMask", 1);
        linkedPortal.screen.SetPropertyBlock(block);

        screen.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
    }

    // 在所有传送门渲染完毕，但在玩家摄像机渲染之前调用
    public void PostPortalRender()
    {
        if (!enabled) return; // 拦截未正确初始化的组件
        foreach (var traveller in trackedTravellers)
        {
            UpdateSliceParams(traveller);
        }
        ProtectScreenFromClipping(playerCam.transform.position);
    }

    // 碰撞触发时，所有挂载 PortalTraveller.cs 的物体加入正在穿越者列表
    void OnTriggerEnter(Collider other)
    {
        var traveller = other.GetComponent<PortalTraveller>();
        if (traveller)
        {
            OnTravellerEnterPortal(traveller);
        }
    }

    // 碰撞结束时：
    void OnTriggerExit(Collider other)
    {
        var traveller = other.GetComponent<PortalTraveller>();
        if (traveller && trackedTravellers.Contains(traveller))
        {
            traveller.ExitPortalThreshold();
            trackedTravellers.Remove(traveller);
        }
    }

    // 生命周期管理（防泄漏） 
    // 依据：RenderTexture 是非托管资源，组件禁用或销毁时必须手动清理，防止场景切换导致显存泄漏。
    void OnDisable()
    {
        ReleaseTextures();
    }

    void OnDestroy()
    {
        ReleaseTextures();
    }
    #region 功能函数

    // 创建 RT、将摄像机画面赋值给纹理
    // ================= 替换原有的 CreateViewTexture =================
    void CreateViewTexture()
    {
        // 步骤 1：判断是否需要重建纹理
        // 依据：当 viewTexture 不存在、屏幕分辨率改变，或开启嵌套但临时缓冲未分配时，必须触发重建。
        bool requiresRebuild = (viewTexture == null || viewTexture.width != Screen.width || viewTexture.height != Screen.height);

        if (AllowNestingPortal && tempTexture == null)
        {
            requiresRebuild = true;
        }

        // 步骤 2：执行重建逻辑
        if (requiresRebuild)
        {
            // 释放旧资源
            if (viewTexture != null) viewTexture.Release();
            if (tempTexture != null) tempTexture.Release();

            // viewTexture 是无论是否嵌套都必须存在的基础渲染目标
            viewTexture = new RenderTexture(Screen.width, Screen.height, 24);
            portalCam.targetTexture = viewTexture;
            linkedPortal.screen.material.SetTexture("_MainTex", viewTexture);

            // 依据：仅在允许嵌套时，才分配双缓冲需要的 tempTexture，严格控制显存开销
            if (AllowNestingPortal)
            {
                tempTexture = new RenderTexture(Screen.width, Screen.height, 24);
            }
        }
        // 步骤 3：运行时动态释放机制
        // 依据：排查潜在的性能浪费。如果游戏运行中途，开关被脚本或 Inspector 动态关闭，
        // 必须主动释放不再使用的 tempTexture，而不是等 GC 回收。
        else if (!AllowNestingPortal && tempTexture != null)
        {
            tempTexture.Release();
            Destroy(tempTexture);
            tempTexture = null;
        }
    }

    void ReleaseTextures()
    {
        if (viewTexture != null)
        {
            viewTexture.Release();
            Destroy(viewTexture);
            viewTexture = null;
        }
        if (tempTexture != null)
        {
            tempTexture.Release();
            Destroy(tempTexture);
            tempTexture = null;
        }
    }

    // 将碰撞物体加入穿越中列表的具体逻辑
    void OnTravellerEnterPortal(PortalTraveller traveller)
    {
        if (!trackedTravellers.Contains(traveller))
        {
            traveller.EnterPortalThreshold();
            traveller.previousOffsetFromPortal = traveller.transform.position - transform.position;
            trackedTravellers.Add(traveller);
        }
    }

    // 处理正在穿过门的物体：
    // 穿过了：传送到对面
    // 没穿过：实时显示（传送到那边，并在这边保留一个复制体）
    void HandleTravellers()
    {
        // 遍历所有正在通过传送门的物体
        for (int i = 0; i < trackedTravellers.Count; i++)
        {
            PortalTraveller traveller = trackedTravellers[i];
            Transform travellerT = traveller.transform;

            // 不使用逐个物体计算的旋转矩阵，改用相对计算
            // var m = linkedPortal.transform.localToWorldMatrix * transform.worldToLocalMatrix * travellerT.localToWorldMatrix;

            Vector3 offsetFromPortal = travellerT.position - transform.position;
            // 通过对比上一帧和当前帧物体所在的正反面（portalSide 是否等于 portalSideOld）。一旦符号反转，说明物体在这一帧穿过了传送门平面，立刻执行 Teleport。
            int portalSide = System.Math.Sign(Vector3.Dot(offsetFromPortal, transform.forward));
            int portalSideOld = System.Math.Sign(Vector3.Dot(traveller.previousOffsetFromPortal, transform.forward));

            // 如果物体从传送门的一侧跨越到了另一侧，则执行传送
            if (portalSide != portalSideOld)
            {
                var positionOld = travellerT.position;
                var rotOld = travellerT.rotation;

                // ===================== 优化：无矩阵的传送计算（替代原m变量）=====================
                Vector3 localPos = transform.InverseTransformPoint(travellerT.position);
                Quaternion localRot = Quaternion.Inverse(transform.rotation) * travellerT.rotation;
                Vector3 targetPos = linkedPortal.transform.TransformPoint(localPos);
                Quaternion targetRot = linkedPortal.transform.rotation * localRot;

                // 调用物体的传送逻辑
                traveller.Teleport(transform, linkedPortal.transform, targetPos, targetRot);
                traveller.graphicsClone.transform.SetPositionAndRotation(positionOld, rotOld);

                // 不能依赖 OnTriggerEnter/Exit，因为它们依赖于 FixedUpdate 的运行时间
                // 这里将其手动指定到 LateUpdate() 更新
                linkedPortal.OnTravellerEnterPortal(traveller);
                trackedTravellers.RemoveAt(i);
                i--;
            }
            else
            {
                // 如果还未穿过，则更新克隆体在另一侧门的位置，使其看起来像是从那边出来的
                // 1. 计算物体相对于当前传送门的本地位置（兼容所有Unity版本）
                Vector3 localRelativePos = transform.InverseTransformPoint(travellerT.position);
                // 2. 计算物体相对于当前传送门的本地旋转（兼容所有Unity版本，替代InverseTransformRotation）
                Quaternion localRelativeRot = Quaternion.Inverse(transform.rotation) * travellerT.rotation;
                // 3. 转换为链接传送门的世界坐标/旋转
                Vector3 cloneWorldPos = linkedPortal.transform.TransformPoint(localRelativePos);
                Quaternion cloneWorldRot = linkedPortal.transform.rotation * localRelativeRot;

                // 同步克隆体
                traveller.graphicsClone.transform.SetPositionAndRotation(cloneWorldPos, cloneWorldRot);

                traveller.previousOffsetFromPortal = offsetFromPortal;
            }
        }
    }

    // 修复物体穿越问题
    void HandleClipping()
    {
        float near = portalCam.nearClipPlane;
        float halfH = near * Mathf.Tan(portalCam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float halfW = halfH * portalCam.aspect;

        float maxError = Mathf.Sqrt(halfW * halfW + halfH * halfH + near * near);

        float precisionMargin = maxError * 0.01f + 0.001f;

        float safeHideDist = -(maxError + precisionMargin);
        float safeShowDist = maxError + precisionMargin;

        float cloneSafeOffset = precisionMargin * 2f;

        float screenThickness = linkedPortal.ProtectScreenFromClipping(portalCam.transform.position);

        foreach (var traveller in trackedTravellers)
        {
            if (SameSideOfPortal(traveller.transform.position, portalCamPos))
                traveller.SetSliceOffsetDst(safeHideDist, false);
            else
                traveller.SetSliceOffsetDst(safeShowDist, false);

            int cloneSideOfLinkedPortal = -SideOfPortal(traveller.transform.position);
            bool camSameSideAsClone = linkedPortal.SideOfPortal(portalCamPos) == cloneSideOfLinkedPortal;

            if (camSameSideAsClone)
                traveller.SetSliceOffsetDst(cloneSafeOffset, true);
            else
                traveller.SetSliceOffsetDst(-cloneSafeOffset, true);
        }

        foreach (var linkedTraveller in linkedPortal.trackedTravellers)
        {
            var travellerPos = linkedTraveller.graphicsObject.transform.position;
            bool cloneOnSameSideAsCam = linkedPortal.SideOfPortal(travellerPos) != SideOfPortal(portalCamPos);
            if (cloneOnSameSideAsCam)
                linkedTraveller.SetSliceOffsetDst(safeHideDist, true);
            else
                linkedTraveller.SetSliceOffsetDst(safeShowDist, true);

            bool camSameSideAsTraveller = linkedPortal.SameSideOfPortal(linkedTraveller.transform.position, portalCamPos);
            if (camSameSideAsTraveller)
                linkedTraveller.SetSliceOffsetDst(cloneSafeOffset, false);
            else
                linkedTraveller.SetSliceOffsetDst(-cloneSafeOffset, false);
        }
    }

    // 设置传送门屏幕的厚度，防止玩家穿过时摄像机近裁剪面与屏幕发生裁剪（穿模）
    float ProtectScreenFromClipping(Vector3 viewPoint)
    {
        // World-space thickness we actually need
        float worldThickness = playerCam.nearClipPlane + nearClipOffset;

        Transform screenT = screen.transform;

        // localScale and localPosition are in PARENT space, so divide by the
        // parent's lossyScale.z to keep both values correct in world-space,
        // regardless of how the portal parent is scaled.
        float parentScaleZ = screenT.parent != null ? screenT.parent.lossyScale.z : 1f;
        float localThickness = worldThickness / parentScaleZ;

        screenT.localScale = new Vector3(screenT.localScale.x, screenT.localScale.y, localThickness);

        // Shift screen toward the side the camera is NOT on, so only the front
        // face is ever visible. Same division by parentScaleZ keeps the shift
        // correct in world-space.
        bool camFacingSameDirAsPortal = Vector3.Dot(transform.forward, transform.position - viewPoint) > 0;
        screenT.localPosition = Vector3.forward * (localThickness * 0.5f) * (camFacingSameDirAsPortal ? 1f : -1f);

        return worldThickness; // return world-space value for slice offset calculations
    }

    // 更新穿越的材质
    void UpdateSliceParams(PortalTraveller traveller)
    {
        // 计算切片法线
        int side = SideOfPortal(traveller.transform.position);
        Vector3 sliceNormal = transform.forward * -side;
        Vector3 cloneSliceNormal = linkedPortal.transform.forward * side;

        // 计算切片中心点
        Vector3 slicePos = transform.position;
        Vector3 cloneSlicePos = linkedPortal.transform.position;

        // 调整切片偏移，当玩家站在物体另一侧时，确保切片不会穿透
        float sliceOffsetDst = 0;
        float cloneSliceOffsetDst = 0;
        float screenThickness = screen.transform.localScale.z;

        bool playerSameSideAsTraveller = SameSideOfPortal(playerCam.transform.position, traveller.transform.position);
        if (!playerSameSideAsTraveller)
        {
            sliceOffsetDst = -screenThickness;
        }
        bool playerSameSideAsCloneAppearing = side != linkedPortal.SideOfPortal(playerCam.transform.position);
        if (!playerSameSideAsCloneAppearing)
        {
            cloneSliceOffsetDst = -screenThickness;
        }

        // 将参数应用到旅行者及其克隆体的材质上（通常是 Shader 中的变量）
        for (int i = 0; i < traveller.originalMaterials.Length; i++)
        {
            traveller.originalMaterials[i].SetVector("sliceCentre", slicePos);
            traveller.originalMaterials[i].SetVector("sliceNormal", sliceNormal);
            traveller.originalMaterials[i].SetFloat("sliceOffsetDst", sliceOffsetDst);

            traveller.cloneMaterials[i].SetVector("sliceCentre", cloneSlicePos);
            traveller.cloneMaterials[i].SetVector("sliceNormal", cloneSliceNormal);
            traveller.cloneMaterials[i].SetFloat("sliceOffsetDst", cloneSliceOffsetDst);
        }
    }

    // 创建贴合远程传送门的斜投影近平面：
    // 使用自定义投影矩阵，使传送门摄像机的近裁剪面与传送门表面重合
    // 注意：这会影响深度缓冲区的精度，可能导致屏幕空间 AO 等效果出现问题
    void SetNearClipPlane()
    {
        // 1. 强制同步摄像机基础标量参数，防止 URP Culling 使用旧数据
        portalCam.nearClipPlane = playerCam.nearClipPlane;
        portalCam.farClipPlane = playerCam.farClipPlane;
        portalCam.fieldOfView = playerCam.fieldOfView;
        portalCam.aspect = playerCam.aspect;

        Transform clipPlane = transform;
        int dot = System.Math.Sign(Vector3.Dot(clipPlane.forward, transform.position - portalCam.transform.position));

        Vector3 camSpacePos = portalCam.worldToCameraMatrix.MultiplyPoint(clipPlane.position);
        Vector3 camSpaceNormal = portalCam.worldToCameraMatrix.MultiplyVector(clipPlane.forward) * dot;

        float camSpaceDst = -Vector3.Dot(camSpacePos, camSpaceNormal) + nearClipOffset;

        // 2. 只有在安全距离外，才尝试计算斜裁剪面
        if (Mathf.Abs(camSpaceDst) > nearClipLimit)
        {
            Vector4 clipPlaneCameraSpace = new Vector4(camSpaceNormal.x, camSpaceNormal.y, camSpaceNormal.z, camSpaceDst);
            Matrix4x4 obliqueMatrix = playerCam.CalculateObliqueMatrix(clipPlaneCameraSpace);

            portalCam.projectionMatrix = obliqueMatrix; // 将结果传递给 摄像机
        }
        else
        {
            portalCam.projectionMatrix = playerCam.projectionMatrix;
        }
    }


    #endregion 功能函数

    #region 一些辅助工具

    // 判断点在传送门的哪一侧
    int SideOfPortal(Vector3 pos)
    {
        // 返回 1：在传送门的正面
        // 返回 -1：在传送门的反面
        return System.Math.Sign(Vector3.Dot(pos - transform.position, transform.forward));
    }

    // 判断两个点是否在传送门的同一侧
    bool SameSideOfPortal(Vector3 posA, Vector3 posB)
    {
        return SideOfPortal(posA) == SideOfPortal(posB);
    }

    Vector3 portalCamPos
    {
        get
        {
            return portalCam.transform.position;
        }
    }

    // 在 Unity 编辑器中修改属性时自动关联双向链接
    void OnValidate()
    {
        if (linkedPortal != null)
        {
            linkedPortal.linkedPortal = this;
        }

        // 依据：需求明确指出禁用双缓冲后，嵌套次数强制锁定为1
        if (!AllowNestingPortal)
        {
            recursionLimit = 1;
        }
        else
        {
            // 防御性修正：开启双缓冲时，嵌套次数下限至少应为2（1层本身不需要双缓冲），
            // 但为了兼容性，至少保证其 >= 1，避免因误操作输入负数或0导致渲染循环崩溃。
            if (recursionLimit < 1) recursionLimit = 1;
        }
    }

    // 辅助函数：校验投影矩阵合法性
    bool IsValidProjectionMatrix(Matrix4x4 mat)
    {
        // 排查矩阵中是否存在 NaN 或 Infinity
        for (int i = 0; i < 16; i++)
        {
            if (float.IsNaN(mat[i]) || float.IsInfinity(mat[i]))
            {
                return false;
            }
        }
        // 投影矩阵的行列式不应为 0（允许微小的浮点误差）
        if (Mathf.Abs(mat.determinant) < 1e-6f)
        {
            return false;
        }
        return true;
    }
    #endregion 一些辅助工具
}