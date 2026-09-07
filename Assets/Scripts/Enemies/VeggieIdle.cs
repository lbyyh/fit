using UnityEngine;

namespace Fit.Enemies
{
    /// <summary>
    /// 蔬菜待机摇摆 —— Q 版萌系（ID-006）的低成本实现。
    ///
    /// 【为什么挂在模型子节点而不是根节点】
    /// EnemyBase.FaceTarget 每帧写 transform.rotation 让蔬菜面朝玩家。
    /// 如果摇摆也写在根节点，两者会互相覆盖，表现为"朝向抖动/抽搐"。
    /// 所以根节点归 AI 管朝向，本组件挂在模型子节点上只管表演，互不干扰。
    ///
    /// 同理也不碰 scale —— Telegraph 的前摇膨胀写的是 localScale，改了会打架。
    ///
    /// 纯客户端表现，不影响判定。
    /// </summary>
    public sealed class VeggieIdle : MonoBehaviour
    {
        [Header("摇摆")]
        [SerializeField, Range(0f, 15f)] private float _swayDegrees = 5f;
        [SerializeField] private float _swaySpeed = 1.6f;

        [Header("上下浮动")]
        [SerializeField] private float _bobHeight = 0.04f;

        [Header("随机化")]
        [SerializeField] private bool _randomizePhase = true;

        private float _phase;
        private Vector3 _baseLocalPos;

        private void Awake()
        {
            _phase = _randomizePhase ? Random.Range(0f, Mathf.PI * 2f) : 0f;
            _baseLocalPos = transform.localPosition;
        }

        private void Update()
        {
            float t = Time.time * _swaySpeed + _phase;

            // 摆动周期是浮动的两倍慢，看起来才不像机械振动
            transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t) * _swayDegrees);
            transform.localPosition = _baseLocalPos + Vector3.up * (Mathf.Sin(t * 2f) * _bobHeight);
        }
    }
}
