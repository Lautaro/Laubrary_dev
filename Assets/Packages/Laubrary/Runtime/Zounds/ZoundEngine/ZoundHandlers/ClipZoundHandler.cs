using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Zounds {

    internal class ClipZoundHandler : ZoundHandler<ClipZound> {

        public ClipZoundHandler(ClipZound clipZound, AudioSource audioSource, ZoundArgs zoundArgs) : base(clipZound, audioSource, zoundArgs) {
#if ZOUNDS_CONSIDER_FOLDERS
            if (!ZoundDictionary.runtimeClipFolders.ContainsKey(zound.audioClip)) {
                var folderPath = ZoundRoutings.GetFolderFromClipPath(zound.audioPath);
                ZoundDictionary.runtimeClipFolders.Add(zound.audioClip, folderPath);
            }
#endif
            audioSource.clip = zound.audioClip;

            var zoundRoutings = ZoundsProject.Instance.zoundRoutings;
            var mixerGroup = zoundRoutings.GetRouting(zound
#if ZOUNDS_CONSIDER_FOLDERS
                , zound.audioClip, zound.audioPath
#endif
                );
            audioSource.outputAudioMixerGroup = mixerGroup;
        }

    }

}