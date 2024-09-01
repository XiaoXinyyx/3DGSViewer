# 使用说明
## Quick Start
打开 Unity，在左侧 Hierarchy 窗口中选中 GaussianViewer，此时右侧的 Inspector 窗口会显示此物体拥有的属性，其中 PLY File Path 是 ply 文件的完整路径，需要自己设置此路径，也可以将文件拖动到此选项中
点击上方三角形状的 play 按钮，此时的渲染窗口会切换到 Game 窗口，此窗口无法调节视角，需要点击 Scene 窗口，就可以自由移动相机了（按住右键，使用 wasd）

## 裁剪
在 Scene 窗口中，点亮右上角的球形图标，然后选中 GaussianViewer，会出现一个矩形线框，拖动矩形表面中心位置的锚点，可以裁剪掉线框之外的高斯，执行更改后要点击右侧 Inspector 窗口中的 Apply Changes 按钮，以上操作在 Play Mode 下执行（确保上方三角形状的 play 按钮是点亮的状态）