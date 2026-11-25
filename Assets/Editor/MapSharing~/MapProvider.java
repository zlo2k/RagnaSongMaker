package org.ragnasongmaker.share;

import android.app.Activity;
import android.content.ClipData;
import android.content.Intent;
import android.net.Uri;
import androidx.annotation.Keep;
import androidx.core.content.FileProvider;
import java.io.File;

@Keep
public final class MapProvider extends FileProvider {
    public static void share(Activity activity, String path) {
        Uri uri = FileProvider.getUriForFile(activity, activity.getPackageName() + ".maps", new File(path));
        Intent send = new Intent(Intent.ACTION_SEND);
        send.setType("application/zip");
        send.putExtra(Intent.EXTRA_STREAM, uri);
        send.setClipData(ClipData.newRawUri("ZIP", uri));
        send.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
        activity.startActivity(Intent.createChooser(send, null));
    }
}
