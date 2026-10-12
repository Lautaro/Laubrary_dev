using System;
using System.Collections.Generic;
using System.Linq;
using Laubrary.ZTracker.Engine;
using Laubrary.ZTracker.Model;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    public sealed partial class ZTrackerModelWindow
    {
        static ZTrackerModelWindow overhaulWalk;
        static readonly List<UnityEngine.Object> overhaulFixtures=new List<UnityEngine.Object>();
        public static string CheckOverhaulControls()
        {
            var host=CreateInstance<ZTrackerModelWindow>();host.Show();
            try
            {
            var box=Z.BoxKeyed("Proof","Temporary header toggle test","tracker.overhaul.proof");
            host.rootVisualElement.Add(box);
            bool state=false;box.SetHeaderToggle(false,v=>state=v,"Enable section");box.IsOpen=true;
            if(box.IsOpen||box.contentContainer.style.display!=DisplayStyle.None)throw new Exception("Disabled section unfolded");
            box.Q<Toggle>().value=true;
            if(!state||!box.IsOpen||box.contentContainer.style.display!=DisplayStyle.Flex)throw new Exception("Enabled section did not restore");
            box.Q<Toggle>().value=false;
            if(box.IsOpen)throw new Exception("Disabled section remained unfolded");
            var number=Z.ScrubNumber("Notes",3,1,12,"Proof",v=>{},defaultValue:1);
            number.value=100;if(number.value!=12)throw new Exception("Count upper bound");number.value=-100;if(number.value!=1)throw new Exception("Count lower bound");
            return "PASS section disabled-fold gate, re-enable restore, discrete number bounds";
            }
            finally{host.Close();}
        }
        public static string OpenOverhaulWalk(int workspace=0,int family=1)
        {
            CloseOverhaulWalk();
            var instrument=CreateInstance<ZTrackerInstrument>();instrument.schemaVersion=1;instrument.model=NewInstrumentData();instrument.model.name="Overhaul proof synth";overhaulFixtures.Add(instrument);
            var kit=CreateInstance<ZTrackerInstrument>();kit.schemaVersion=1;kit.model=NewInstrumentData();kit.model.family=InstrumentFamily.DrumKit;kit.model.name="Overhaul proof kit";kit.model.drumKit.mappings.Add(new DrumMapping{id="proof-kick",name="Synth kick",tag="BD",noteMin=36,noteMax=36,baseNote=36,synth=instrument});overhaulFixtures.Add(kit);
            var song=CreateInstance<ZTrackerSong>();song.schemaVersion=1;song.model=TrackerEngineCheck.FixtureSong(instrument,3,16);song.model.name="Overhaul walk";song.model.patterns[0].name="Verse";song.model.tracks[0].name="Synth";song.model.tracks[song.model.tracks.Count-1].name="Master";song.model.instruments.Add(kit);overhaulFixtures.Add(song);
            var second=new TrackData{id="proof-track-2",name="Drums",kind=TrackKind.Sequencer};song.model.tracks.Insert(1,second);foreach(var pattern in song.model.patterns)pattern.tracks.Add(new PatternTrack{trackId=second.id});
            overhaulWalk=CreateInstance<ZTrackerModelWindow>();overhaulWalk.titleContent=new GUIContent("ZTracker overhaul check");overhaulWalk.song=song;overhaulWalk.instrument=family==2?kit:instrument;overhaulWalk.entryInstrument=family==2?1:0;overhaulWalk.pane=workspace;overhaulWalk.position=new Rect(80,90,1250,800);overhaulWalk.Show();overhaulWalk.CreateGUI();overhaulWalk.Focus();
            return "Temporary in-memory walk open; user assets unchanged";
        }
        public static string CheckDrumCataloguePages()
        {
            if(DrumTags.Category("Kicks")!=DrumCategory.Kick||DrumTags.Category("Snares")!=DrumCategory.Snare)throw new Exception("Plural parent folder aliases");
            var asset=CreateInstance<ZTrackerInstrument>();asset.schemaVersion=1;asset.model=NewInstrumentData();asset.model.family=InstrumentFamily.DrumKit;
            var song=CreateInstance<ZTrackerSong>();song.schemaVersion=1;song.model=TrackerEngineCheck.FixtureSong(asset);
            var window=CreateInstance<ZTrackerModelWindow>();
            try
            {
                window.song=song;window.instrument=asset;window.pane=3;window.Show();window.CreateGUI();window.drumItems.Clear();
                for(int i=0;i<205;i++)window.drumItems.Add(new DrumBrowserItem{name="Sound "+i,path="fixture:"+i,tag=i<101?"Bass Drum":"Texture"});
                window.drumSearch="";window.drumTagFilter="";
                for(int page=0;page<3;page++){window.drumPage=page;window.FilterDrumBrowser();if(window.drumCards.childCount!=(page==2?5:100))throw new Exception("Catalogue page "+page+" missing sources");}
                window.drumTagFilter="Texture";window.drumPage=0;window.FilterDrumBrowser();if(window.drumCards.childCount!=100)throw new Exception("Custom parent tag not matched");window.drumPage=1;window.FilterDrumBrowser();if(window.drumCards.childCount!=4)throw new Exception("Custom tag final page");
                window.drumTagFilter="KD";window.drumPage=1;window.FilterDrumBrowser();if(window.drumCards.childCount!=1)throw new Exception("Alias tag final page");
                window.drumTagFilter="";window.drumSearch="bassdrum";window.drumPage=0;window.FilterDrumBrowser();if(window.drumCards.childCount!=100)throw new Exception("Alias search");
                return "PASS 205 sounds across all pages, last-page access, custom parent tags and alias search";
            }
            finally{window.Close();DestroyImmediate(song);DestroyImmediate(asset);}
        }
        public static void CloseOverhaulWalk()
        {
            // A script reload clears static ownership; recover only our explicitly titled fixtures.
            foreach(var window in Resources.FindObjectsOfTypeAll<ZTrackerModelWindow>())
            {
                if(window.titleContent.text!="ZTracker overhaul check")continue;
                if(window.song!=null&&string.IsNullOrEmpty(AssetDatabase.GetAssetPath(window.song)))
                {
                    foreach(var item in window.song.model.instruments)if(item!=null&&string.IsNullOrEmpty(AssetDatabase.GetAssetPath(item))&&!overhaulFixtures.Contains(item))overhaulFixtures.Add(item);
                    if(!overhaulFixtures.Contains(window.song))overhaulFixtures.Add(window.song);
                }
                window.Close();
            }
            overhaulWalk=null;
            foreach(var asset in overhaulFixtures)if(asset!=null)DestroyImmediate(asset);overhaulFixtures.Clear();
        }
    }
}
