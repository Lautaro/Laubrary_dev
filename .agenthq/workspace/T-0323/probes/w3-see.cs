var sb=new System.Text.StringBuilder();
var w=ZWin("SpriteFxStackWindow"); if (w==null) return "NO WINDOW";
w.position=new Rect(20,20,900,880); w.titleContent=new GUIContent("SpriteFxStackWindow");
return "opened, pos="+w.position;
