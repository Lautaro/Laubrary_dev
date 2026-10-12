using System;
using Laubrary.ZTracker.Model;
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    public sealed partial class ZTrackerModelWindow
    {
        VisualElement patternTrackSettings;

        void BuildPatternSettings(VisualElement tools)
        {
            tools.Add(Named(Z.ScrubNumber("Length",Pattern.lineCount,1,512,"Number of rows in this pattern; Undo restores cropped rows and automation.",v=>SongEdit("pattern length",()=>{ZTrackerPatternOperations.Resize(Pattern,v);row=Mathf.Min(row,v-1);},true),width:115,defaultValue:64),"pattern-length"));
            patternTrackSettings=new VisualElement{name="pattern-track-settings"};tools.Add(patternTrackSettings);RefreshPatternTrackSettings();
        }

        void RefreshPatternTrackSettings()
        {
            if(patternTrackSettings==null||patternTrackSettings.panel==null&&patternTrackSettings.parent==null)return;
            patternTrackSettings.Clear();var t=SelectedTrack;if(t==null)return;
            patternTrackSettings.Add(Named(Z.Field("Name","Rename the selected track.",Z.TextInput(t.name,"Rename the selected track.",v=>{SongEdit("track name",()=>t.name=v);for(int i=0;i<Data.tracks.Count;i++)if(Data.tracks[i].id==t.id){var header=gridCanvas?.Q<Label>("pattern-track-header-"+i);if(header!=null)header.text=v;break;}},88)),"pattern-track-name"));
            if(t.kind==TrackKind.Sequencer)
                patternTrackSettings.Add(Named(Z.ScrubNumber("Notes",t.visibleNoteColumns,1,12,"Number of visible note columns; hidden note data stays stored.",v=>SongEdit("note columns",()=>t.visibleNoteColumns=v,true),width:115,defaultValue:1),"pattern-note-columns"));
            patternTrackSettings.Add(Named(Z.ScrubNumber("FX",t.visibleEffectColumns,0,8,"Number of visible track effect columns; hidden commands stay stored.",v=>SongEdit("effect columns",()=>t.visibleEffectColumns=v,true),width:115,defaultValue:1),"pattern-fx-columns"));
        }

        void ReorderTracks(int from,int to)
        {
            if(from<0||to<0||from>=Data.tracks.Count||to>=Data.tracks.Count||from==to)return;
            string cursorTrack=pane==0&&fields.Count>0?Data.tracks[fields[sub].track].id:null;
            int cursorColumn=pane==0&&fields.Count>0?fields[sub].column:0;
            FieldKind cursorKind=pane==0&&fields.Count>0?fields[sub].kind:FieldKind.Note;
            SongEdit("reorder tracks",()=>{var selected=SelectedTrack;Move(Data.tracks,from,to);track=Data.tracks.IndexOf(selected);},true);
            if(cursorTrack!=null){int current=fields.FindIndex(f=>Data.tracks[f.track].id==cursorTrack&&f.column==cursorColumn&&f.kind==cursorKind);if(current>=0){sub=current;RefreshCells();RefreshCellDetails();}}
        }

        void PatternShortcutFromControls(KeyDownEvent e)
        {
            if(HandleTrackerShortcut(e)){e.StopPropagation();e.PreventDefault();}
        }

        // The root can call this before instrument keyboard handling so transport/edit shortcuts
        // work from toolbar controls too. Text entry keeps its normal typing and cursor behavior.
        internal bool HandleTrackerShortcut(KeyDownEvent e)
        {
            return Data!=null&&!e.ctrlKey&&!e.commandKey&&!e.altKey&&!IsTyping(e.target as VisualElement)&&!IsTyping(rootVisualElement.focusController?.focusedElement as VisualElement)&&HandlePatternShortcut(e);
        }

        bool HandlePatternShortcut(KeyDownEvent e)
        {
            if(e.keyCode==KeyCode.Space){if(playing)StopPreview();else Play(false);return true;}
            if(e.keyCode==KeyCode.Escape){CloseFxSuggestions();editMode=!editMode;anchorRow=-1;var toggle=controls?.Q("edit-mode");if(toggle!=null)BuildPane();else RefreshCells();return true;}
            if(e.keyCode==KeyCode.Tab&&pane==0&&Pattern!=null&&fields.Count>0){Select(Mathf.Clamp(row+(e.shiftKey?-step:step),0,Pattern.lineCount-1),sub,false);return true;}
            int activeOctave=pane==3&&Instrument!=null&&Instrument.family==InstrumentFamily.DrumKit?drumFirstOctave:octave;
            if(e.keyCode==KeyCode.Plus||e.keyCode==KeyCode.KeypadPlus||e.character=='+'){SetPatternOctave(activeOctave+1);return true;}
            if(e.keyCode==KeyCode.Minus||e.keyCode==KeyCode.KeypadMinus||e.character=='-'){SetPatternOctave(activeOctave-1);return true;}
            return false;
        }

        void SetPatternOctave(int value)
        {
            if(pane==3&&Instrument!=null&&Instrument.family==InstrumentFamily.DrumKit){ReleaseInstrumentKeys();drumFirstOctave=Mathf.Clamp(value,0,8);BuildPane();return;}
            if(pane==3)ReleaseInstrumentKeys();
            octave=Mathf.Clamp(value,0,9);
            if(pane==0||pane==3)BuildPane();
        }
    }
}
