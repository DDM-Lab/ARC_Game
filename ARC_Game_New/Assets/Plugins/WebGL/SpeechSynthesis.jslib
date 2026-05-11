mergeInto(LibraryManager.library, {
    SpeakText: function(textPtr) {
        var text = UTF8ToString(textPtr);
        window.speechSynthesis.cancel();

        var utterance = new SpeechSynthesisUtterance(text);
        utterance.rate  = 0.95;
        utterance.pitch = 1.1;

        // Preferred voices in priority order (female / neutral)
        var preferred = [
            "Google UK English Female",
            "Microsoft Zira Desktop",
            "Samantha",
            "Karen",
            "Moira",
            "Fiona",
            "Victoria"
        ];

        var voices = window.speechSynthesis.getVoices();

        // Try preferred list first
        var picked = null;
        for (var i = 0; i < preferred.length; i++) {
            for (var j = 0; j < voices.length; j++) {
                if (voices[j].name === preferred[i]) { picked = voices[j]; break; }
            }
            if (picked) break;
        }

        // Fall back to any English female voice
        if (!picked) {
            for (var j = 0; j < voices.length; j++) {
                var name = voices[j].name.toLowerCase();
                if (voices[j].lang.startsWith("en") && name.indexOf("female") !== -1) {
                    picked = voices[j]; break;
                }
            }
        }

        if (picked) utterance.voice = picked;

        window.speechSynthesis.speak(utterance);
    },

    CancelSpeech: function() {
        window.speechSynthesis.cancel();
    },

    IsSpeaking: function() {
        return window.speechSynthesis.speaking ? 1 : 0;
    }
});
