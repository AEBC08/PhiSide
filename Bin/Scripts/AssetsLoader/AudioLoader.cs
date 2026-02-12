using Godot;
using System;
using System.IO;

namespace AudioLoader
{
    public static class GodotAudioLoader
    {
        public static AudioStream LoadAudioFile(string path)
            {
        	    using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        	    if (file == null)
        	    {
        		    GD.PrintErr("文件打开失败: ", Godot.FileAccess.GetOpenError());
        		    return null;
        	    }
        
        	    byte[] data = file.GetBuffer((long)file.GetLength());
        	    string extension = Path.GetExtension(path).ToLower();
        
        	    switch (extension)
        	    {
        		    case ".wav":
        			    return LoadWavFile(data);
        		    case ".ogg":
        			    return AudioStreamOggVorbis.LoadFromBuffer(data);
        		    case ".mp3":
        			    return new AudioStreamMP3 { Data = data };
        		    default:
        			    GD.PrintErr($"不支持的音频格式: {extension}");
        			    return null;
        	    }
            }
        
            public static AudioStreamWav LoadWavFile(byte[] rawData)
            {
        	    try
        	    {
        		    short channels = BitConverter.ToInt16(rawData, 22);
        		    int sampleRate = BitConverter.ToInt32(rawData, 24);
        		    short bitsPerSample = BitConverter.ToInt16(rawData, 34);
        
        		    GD.Print($"参数: 采样率={sampleRate}, 位深={bitsPerSample}, 声道={channels}");
        
        		    var audioStream = new AudioStreamWav();
        		    audioStream.Data = rawData;
        		    audioStream.Stereo = (channels == 2);
        		    audioStream.MixRate = sampleRate;
        		    audioStream.Format = (bitsPerSample == 8)
        			    ? AudioStreamWav.FormatEnum.Format8Bits
        			    : AudioStreamWav.FormatEnum.Format16Bits;
        		    return audioStream;
        	    }
        	    catch (Exception e)
        	    {
        		    GD.PrintErr("WAV解析失败: ", e.Message);
        		    return null;
        	    }
            }
    }
}