/*
 * Тонкая обёртка над libvorbis: PCM s16 -> OGG Vorbis в память.
 * Используется из C# через P/Invoke (DllImport "ragnaogg").
 */
#include <stdlib.h>
#include <string.h>
#include <ogg/ogg.h>
#include <vorbis/vorbisenc.h>

typedef struct
{
    unsigned char *data;
    size_t size;
    size_t cap;
} mem_buffer;

static int append_page(mem_buffer *out, ogg_page *page)
{
    size_t need = out->size + (size_t)page->header_len + (size_t)page->body_len;
    if (need > out->cap)
    {
        size_t cap = out->cap ? out->cap : 65536;
        while (cap < need)
            cap *= 2;

        unsigned char *grown = (unsigned char *)realloc(out->data, cap);
        if (!grown)
            return -1;

        out->data = grown;
        out->cap = cap;
    }

    memcpy(out->data + out->size, page->header, (size_t)page->header_len);
    out->size += (size_t)page->header_len;
    memcpy(out->data + out->size, page->body, (size_t)page->body_len);
    out->size += (size_t)page->body_len;
    return 0;
}

static int flush_pages(ogg_stream_state *stream, mem_buffer *out, int force)
{
    ogg_page page;
    for (;;)
    {
        int has = force ? ogg_stream_flush(stream, &page) : ogg_stream_pageout(stream, &page);
        if (!has)
            break;

        if (append_page(out, &page) != 0)
            return -1;

        force = 0;
    }
    return 0;
}

/*
 * Возвращает malloc'ed буфер с ogg-файлом, размер в *out_size.
 * Качество: -0.1..1.0 (0.4 — примерно 128 kbps стерео).
 * Возвращает NULL при ошибке. Освобождать через ragna_free_ogg.
 */
unsigned char *ragna_encode_ogg(const short *pcm, int frames, int channels, int sample_rate,
                                float quality, int *out_size)
{
    if (out_size)
        *out_size = 0;
    if (!pcm || frames <= 0 || channels <= 0 || sample_rate <= 0 || !out_size)
        return NULL;

    vorbis_info info;
    vorbis_comment comment;
    vorbis_dsp_state dsp;
    vorbis_block block;
    ogg_stream_state stream;
    ogg_packet packet;
    ogg_packet header_main;
    ogg_packet header_comments;
    ogg_packet header_codebooks;

    mem_buffer out;
    memset(&out, 0, sizeof(out));

    vorbis_info_init(&info);
    if (vorbis_encode_init_vbr(&info, channels, sample_rate, quality) != 0)
    {
        vorbis_info_clear(&info);
        return NULL;
    }

    vorbis_comment_init(&comment);
    vorbis_comment_add_tag(&comment, "ENCODER", "ragnaGod/libvorbis");

    vorbis_analysis_init(&dsp, &info);
    vorbis_block_init(&dsp, &block);

    ogg_stream_init(&stream, (int)(sample_rate ^ frames ^ channels));

    if (vorbis_analysis_headerout(&dsp, &comment, &header_main, &header_comments, &header_codebooks) != 0)
        goto fail;

    ogg_stream_packetin(&stream, &header_main);
    ogg_stream_packetin(&stream, &header_comments);
    ogg_stream_packetin(&stream, &header_codebooks);

    if (flush_pages(&stream, &out, 1) != 0)
        goto fail;

    {
        int written = 0;
        while (written < frames)
        {
            int chunk = frames - written;
            if (chunk > 1024)
                chunk = 1024;

            float **buffer = vorbis_analysis_buffer(&dsp, chunk);
            if (!buffer)
                goto fail;

            for (int i = 0; i < chunk; i++)
            {
                for (int c = 0; c < channels; c++)
                    buffer[c][i] = (float)pcm[(size_t)(written + i) * channels + c] / 32768.0f;
            }

            vorbis_analysis_wrote(&dsp, chunk);
            written += chunk;

            while (vorbis_analysis_blockout(&dsp, &block) == 1)
            {
                vorbis_analysis(&block, NULL);
                vorbis_bitrate_addblock(&block);

                while (vorbis_bitrate_flushpacket(&dsp, &packet))
                {
                    ogg_stream_packetin(&stream, &packet);
                    if (flush_pages(&stream, &out, 0) != 0)
                        goto fail;
                }
            }
        }
    }

    vorbis_analysis_wrote(&dsp, 0);
    while (vorbis_analysis_blockout(&dsp, &block) == 1)
    {
        vorbis_analysis(&block, NULL);
        vorbis_bitrate_addblock(&block);

        while (vorbis_bitrate_flushpacket(&dsp, &packet))
        {
            ogg_stream_packetin(&stream, &packet);
            if (flush_pages(&stream, &out, 0) != 0)
                goto fail;
        }
    }

    flush_pages(&stream, &out, 1);

    ogg_stream_clear(&stream);
    vorbis_block_clear(&block);
    vorbis_dsp_clear(&dsp);
    vorbis_comment_clear(&comment);
    vorbis_info_clear(&info);

    *out_size = (int)out.size;
    return out.data;

fail:
    ogg_stream_clear(&stream);
    vorbis_block_clear(&block);
    vorbis_dsp_clear(&dsp);
    vorbis_comment_clear(&comment);
    vorbis_info_clear(&info);
    if (out.data)
        free(out.data);
    return NULL;
}

void ragna_free_ogg(unsigned char *buffer)
{
    if (buffer)
        free(buffer);
}
