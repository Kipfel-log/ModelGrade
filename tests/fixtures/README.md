# 真机样本制作

仓库当前没有真实 SOLIDWORKS 样本。阶段 0 只需要一份脱敏的独立零件，建议用 SOLIDWORKS 另建样本，避免带入班级作业、宏或外部文档引用。

1. 新建公制零件，建立 100 mm × 50 mm 的矩形，拉伸成 10 mm 厚实体。保存为 `reference-plate.SLDPRT`。记下活动配置名和 CAD 界面的质量属性读数。样本尺寸只用于制作。
2. 保留一份无材料样本用于检查质量留空，再另存一份并赋予已知材料用于检查材料、质量和密度。
3. 需要扩充样本时，复制标准件生成 `exact-copy.SLDPRT`。另存并修改厚度生成 `wrong-thickness.SLDPRT`。
4. 未来录入几何对照样本时，先制作带通孔的标准板，再在相同板件上平移同径通孔生成 `same-volume-wrong-hole.SLDPRT`。将整个参考几何相对原点平移生成 `shifted-origin.SLDPRT`。
5. 用不合并的拉伸生成 `multi-body.SLDPRT`，用仅含草图或曲面的零件检查零实体分支。使用无隐私内容的普通文本另建 `invalid-or-corrupt.SLDPRT` 来测试打开失败。

2025 验收样本应使用 2025 创建，或在 CAD 中明确保存为目标版本后的独立副本。分别记录现场实际观察的数值与警告。
