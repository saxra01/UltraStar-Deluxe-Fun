﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UniInject;
using UnityEngine;
using UnityEngine.UI;
using UniRx;
using Button = UnityEngine.UIElements.Button;

// Disable warning about fields that are never assigned, their values are injected.
#pragma warning disable CS0649

public class SongEditorSceneControl : MonoBehaviour, IBinder, INeedInjection
{
    [InjectedInInspector]
    public SongAudioPlayer songAudioPlayer;

    [InjectedInInspector]
    public SongVideoPlayer songVideoPlayer;

    [InjectedInInspector]
    public SongEditorNoteRecorder songEditorNoteRecorder;

    [InjectedInInspector]
    public SongEditorSelectionControl selectionControl;

    [InjectedInInspector]
    public NoteArea noteArea;

    [InjectedInInspector]
    public NoteAreaDragHandler noteAreaDragHandler;
    
    [InjectedInInspector]
    public NoteAreaContextMenuHandler noteAreaContextMenuHandler;

    [InjectedInInspector]
    public EditorNoteDisplayer editorNoteDisplayer;

    [InjectedInInspector]
    public MicPitchTracker micPitchTracker;

    [InjectedInInspector]
    public Canvas canvas;

    [InjectedInInspector]
    public GraphicRaycaster graphicRaycaster;

    [InjectedInInspector]
    public SongEditorHistoryManager historyManager;

    [InjectedInInspector]
    public SongEditorLayerManager songEditorLayerManager;

    [InjectedInInspector]
    public SongEditorMidiFileImporter midiFileImporter;

    [InjectedInInspector]
    public SongEditorCopyPasteManager songEditorCopyPasteManager;
    
    [InjectedInInspector]
    public LyricsArea lyricsArea;

    [Inject]
    private Injector injector;

    [Inject(UxmlName = R.UxmlNames.togglePlaybackButton)]
    private Button togglePlaybackButton;

    [Inject(UxmlName = R.UxmlNames.toggleRecordingButton)]
    private Button toggleRecordingButton;

    [Inject(UxmlName = R.UxmlNames.undoButton)]
    private Button undoButton;

    [Inject(UxmlName = R.UxmlNames.redoButton)]
    private Button redoButton;

    [Inject(UxmlName = R.UxmlNames.exitSceneButton)]
    private Button exitSceneButton;

    [Inject(UxmlName = R.UxmlNames.saveButton)]
    private Button saveButton;

    private readonly SongMetaChangeEventStream songMetaChangeEventStream = new SongMetaChangeEventStream();

    private double positionInSongInMillisWhenPlaybackStarted;

    private readonly Dictionary<Voice, Color> voiceToColorMap = new Dictionary<Voice, Color>();

    private bool audioWaveFormInitialized;

    public double StopPlaybackAfterPositionInSongInMillis { get; set; }

    private OverviewAreaControl overviewAreaControl;
    private VideoAreaControl videoAreaControl;
    private SongEditorVirtualPianoControl songEditorVirtualPianoControl;

    public SongMeta SongMeta
    {
        get
        {
            return SceneData.SelectedSongMeta;
        }
    }

    private SongEditorSceneData sceneData;
    public SongEditorSceneData SceneData
    {
        get
        {
            if (sceneData == null)
            {
                sceneData = SceneNavigator.Instance.GetSceneDataOrThrow<SongEditorSceneData>();
            }
            return sceneData;
        }
    }

    public List<IBinding> GetBindings()
    {
        BindingBuilder bb = new BindingBuilder();
        // Note that the SceneData and SongMeta are loaded on access here if not done yet.
        bb.BindExistingInstance(SceneData);
        bb.BindExistingInstance(SongMeta);
        bb.BindExistingInstance(songAudioPlayer);
        bb.BindExistingInstance(songVideoPlayer);
        bb.BindExistingInstance(noteArea);
        bb.BindExistingInstance(noteAreaDragHandler);
        bb.BindExistingInstance(noteAreaContextMenuHandler);
        bb.BindExistingInstance(songEditorLayerManager);
        bb.BindExistingInstance(micPitchTracker);
        bb.BindExistingInstance(songEditorNoteRecorder);
        bb.BindExistingInstance(selectionControl);
        bb.BindExistingInstance(editorNoteDisplayer);
        bb.BindExistingInstance(canvas);
        bb.BindExistingInstance(graphicRaycaster);
        bb.BindExistingInstance(historyManager);
        bb.BindExistingInstance(songMetaChangeEventStream);
        bb.BindExistingInstance(lyricsArea);
        bb.BindExistingInstance(midiFileImporter);
        bb.BindExistingInstance(songEditorCopyPasteManager);
        bb.BindExistingInstance(this);
        return bb.GetBindings();
    }

    void Awake()
    {
        Debug.Log($"Start editing of '{SceneData.SelectedSongMeta.Title}' at {SceneData.PositionInSongInMillis} ms.");

        songAudioPlayer.Init(SongMeta);
        songVideoPlayer.SongMeta = SongMeta;

        songAudioPlayer.PositionInSongInMillis = SceneData.PositionInSongInMillis;
    }

    void Start()
    {
        overviewAreaControl = injector.CreateAndInject<OverviewAreaControl>();
        videoAreaControl = injector.CreateAndInject<VideoAreaControl>();
        songEditorVirtualPianoControl = injector.CreateAndInject<SongEditorVirtualPianoControl>();

        songAudioPlayer.PlaybackStartedEventStream.Subscribe(OnAudioPlaybackStarted);
        songAudioPlayer.PlaybackStoppedEventStream.Subscribe(OnAudioPlaybackStopped);

        togglePlaybackButton.RegisterCallbackButtonTriggered(() => ToggleAudioPlayPause());
        toggleRecordingButton.RegisterCallbackButtonTriggered(() =>
        {
            songEditorNoteRecorder.IsRecordingEnabled = !songEditorNoteRecorder.IsRecordingEnabled;
            if (songEditorNoteRecorder.IsRecordingEnabled)
            {
                toggleRecordingButton.AddToClassList("recording");
            }
            else
            {
                toggleRecordingButton.RemoveFromClassList("recording");
            }
        });
        exitSceneButton.RegisterCallbackButtonTriggered(() => ReturnToLastScene());
        undoButton.RegisterCallbackButtonTriggered(() => historyManager.Undo());
        redoButton.RegisterCallbackButtonTriggered(() => historyManager.Redo());
        saveButton.RegisterCallbackButtonTriggered(() => SaveSong());
    }

    private void OnAudioPlaybackStopped(double positionInSongInMillis)
    {
        // Jump to last position in song when playback stops
        songAudioPlayer.PositionInSongInMillis = positionInSongInMillisWhenPlaybackStarted;
    }

    private void OnAudioPlaybackStarted(double positionInSongInMillis)
    {
        positionInSongInMillisWhenPlaybackStarted = positionInSongInMillis;
    }

    void Update()
    {
        // Automatically stop playback after a given threshold (e.g. only play the selected notes)
        if (songAudioPlayer.IsPlaying
            && StopPlaybackAfterPositionInSongInMillis > 0
            && songAudioPlayer.PositionInSongInMillis > StopPlaybackAfterPositionInSongInMillis)
        {
            songAudioPlayer.PauseAudio();
            StopPlaybackAfterPositionInSongInMillis = 0;
        }
    }

    public Color GetColorForVoice(Voice voice)
    {
        if (voiceToColorMap.TryGetValue(voice, out Color color))
        {
            return color;
        }
        else
        {
            // Define colors for the voices.
            CreateVoiceToColorMap();
            return voiceToColorMap[voice];
        }
    }

    // Returns the notes in the song as well as the notes in the layers in no particular order.
    public List<Note> GetAllNotes()
    {
        List<Note> result = new List<Note>();
        List<Note> notesInVoices = SongMetaUtils.GetAllNotes(SongMeta);
        List<Note> notesInLayers = songEditorLayerManager.GetAllNotes();
        result.AddRange(notesInVoices);
        result.AddRange(notesInLayers);
        return result;
    }
    
    public List<Note> GetAllVisibleNotes()
    {
        List<Note> result = new List<Note>();
        List<Note> notesInVoices = SongMetaUtils.GetAllNotes(SongMeta)
            .Where(note => songEditorLayerManager.IsVisible(note))
            .ToList();
        List<Note> notesInLayers = songEditorLayerManager.GetAllNotes();
        result.AddRange(notesInVoices);
        result.AddRange(notesInLayers);
        return result;
    }

    private void CreateVoiceToColorMap()
    {
        List<Color32> colors = new List<Color32> {
            ThemeManager.GetColor(R.Color.deviceColor_1),
            ThemeManager.GetColor(R.Color.deviceColor_2),
            ThemeManager.GetColor(R.Color.deviceColor_3),
            ThemeManager.GetColor(R.Color.deviceColor_4),
            ThemeManager.GetColor(R.Color.deviceColor_5),
            ThemeManager.GetColor(R.Color.deviceColor_6)
        };
        int index = 0;
        foreach (Voice v in SongMeta.GetVoices())
        {
            if (index < colors.Count)
            {
                voiceToColorMap[v] = colors[index];
            }
            else
            {
                // fallback color
                voiceToColorMap[v] = Colors.beige;
            }
            index++;
        }
    }

    public void OnBackButtonClicked()
    {
        ReturnToLastScene();
    }

    public void OnSaveButtonClicked()
    {
        SaveSong();
    }

    public void SaveSong()
    {
        string songFile = SongMeta.Directory + Path.DirectorySeparatorChar + SongMeta.Filename;

        // Create backup of original file if not done yet.
        if (SettingsManager.Instance.Settings.SongEditorSettings.SaveCopyOfOriginalFile)
        {
            CreateCopyOfFile(songFile);
        }

        try
        {
            // Write the song data structure to the file.
            UltraStarSongFileWriter.WriteFile(songFile, SongMeta);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            UiManager.Instance.CreateNotification("Saving the file failed:\n" + e.Message);
            return;
        }

        UiManager.Instance.CreateNotification("Saved file");
    }

    private void CreateCopyOfFile(string filePath)
    {
        try
        {
            string backupFile = SongMeta.Directory + Path.DirectorySeparatorChar + SongMeta.Filename.Replace(".txt", ".txt.bak");
            if (File.Exists(backupFile))
            {
                return;
            }
            File.Copy(filePath, backupFile);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            UiManager.Instance.CreateNotification("Creating a copy of the original file failed:\n" + e.Message);
            return;
        }

        UiManager.Instance.CreateNotification("Created copy of original file");
    }

    public void ContinueToSingScene()
    {
        SingSceneData singSceneData;
        if (sceneData.PreviousSceneData is SingSceneData)
        {
            singSceneData = sceneData.PreviousSceneData as SingSceneData;
        }
        else
        {
            singSceneData = new SingSceneData();
            singSceneData.SelectedSongMeta = sceneData.SelectedSongMeta;
            singSceneData.SelectedPlayerProfiles = sceneData.SelectedPlayerProfiles;
            singSceneData.PlayerProfileToMicProfileMap = sceneData.PlayerProfileToMicProfileMap;
        }
        singSceneData.PositionInSongInMillis = songAudioPlayer.PositionInSongInMillis;
        SceneNavigator.Instance.LoadScene(EScene.SingScene, sceneData.PreviousSceneData);
    }

    public void ContinueToSongSelectScene()
    {
        SongSelectSceneData songSelectSceneData;
        if (sceneData.PreviousSceneData is SongSelectSceneData)
        {
            songSelectSceneData = sceneData.PreviousSceneData as SongSelectSceneData;
        }
        else
        {
            songSelectSceneData = new SongSelectSceneData();
            songSelectSceneData.SongMeta = sceneData.SelectedSongMeta;
        }
        SceneNavigator.Instance.LoadScene(EScene.SongSelectScene, songSelectSceneData);
    }

    public void ReturnToLastScene()
    {
        if (sceneData.PreviousSceneData is SingSceneData)
        {
            ContinueToSingScene();
            return;
        }
        ContinueToSongSelectScene();
    }
    
    public void ToggleAudioPlayPause()
    {
        if (songAudioPlayer.IsPlaying)
        {
            songAudioPlayer.PauseAudio();
        }
        else
        {
            songAudioPlayer.PlayAudio();
        }
    }
    
    public void StartEditingNoteText()
    {
        List<Note> selectedNotes = selectionControl.GetSelectedNotes();
        if (selectedNotes.Count == 1)
        {
            Note selectedNote = selectedNotes.FirstOrDefault();
            EditorUiNote uiNote = editorNoteDisplayer.GetUiNoteForNote(selectedNote);
            if (uiNote != null)
            {
                uiNote.StartEditingNoteText();
            }
        }
    }
}
