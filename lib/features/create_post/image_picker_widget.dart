import 'package:app_settings/app_settings.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:image_picker/image_picker.dart';

class ImagePickerWidget extends StatelessWidget {
  const ImagePickerWidget({
    super.key,
    required this.images,
    required this.onPick,
    required this.onRemove,
    this.imageError,
    this.permissionDenied = false,
    this.uploadFailed = false,
    this.onRetry,
  });

  final List<XFile> images;
  final VoidCallback onPick;
  final ValueChanged<int> onRemove;
  final String? imageError;
  final bool permissionDenied;
  final bool uploadFailed;
  final VoidCallback? onRetry;

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;
    final theme = Theme.of(context);

    if (permissionDenied) {
      return Container(
        width: double.infinity,
        padding: const EdgeInsets.all(12),
        decoration: BoxDecoration(
          color: colorScheme.errorContainer.withValues(alpha: 0.4),
          borderRadius: BorderRadius.circular(12),
          border: Border.all(color: colorScheme.error.withValues(alpha: 0.4)),
        ),
        child: Row(
          children: [
            Icon(Icons.no_photography_outlined, color: colorScheme.error),
            const SizedBox(width: 12),
            Expanded(
              child: Text(
                'Fotoğraf izni verilmedi.',
                style: theme.textTheme.bodySmall
                    ?.copyWith(color: colorScheme.error),
              ),
            ),
            TextButton(
              onPressed: () => AppSettings.openAppSettings(
                type: AppSettingsType.settings,
              ),
              child: const Text('Ayarlara Git'),
            ),
          ],
        ),
      );
    }

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        if (images.isNotEmpty)
          _ImageThumbnail(
            file: images.first,
            onRemove: () => onRemove(0),
          )
        else
          OutlinedButton.icon(
            onPressed: onPick,
            style: OutlinedButton.styleFrom(
              minimumSize: const Size.fromHeight(56),
              shape: RoundedRectangleBorder(
                borderRadius: BorderRadius.circular(12),
              ),
              side: imageError != null
                  ? BorderSide(color: colorScheme.error)
                  : null,
            ),
            icon: const Icon(Icons.add_photo_alternate_outlined),
            label: const Text('Fotoğraf Ekle'),
          ),
        if (imageError != null) ...[
          const SizedBox(height: 6),
          Text(
            imageError!,
            style:
                theme.textTheme.bodySmall?.copyWith(color: colorScheme.error),
          ),
        ],
        if (uploadFailed) ...[
          const SizedBox(height: 8),
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
            decoration: BoxDecoration(
              color: colorScheme.errorContainer.withValues(alpha: 0.2),
              borderRadius: BorderRadius.circular(8),
            ),
            child: Row(
              children: [
                Icon(Icons.cloud_off_outlined, size: 16, color: colorScheme.error),
                const SizedBox(width: 8),
                Expanded(
                  child: Text(
                    'Bazı görseller yüklenemedi.',
                    style: TextStyle(color: colorScheme.error, fontSize: 12),
                  ),
                ),
                TextButton(
                  onPressed: onRetry,
                  style: TextButton.styleFrom(visualDensity: VisualDensity.compact),
                  child: const Text('Tekrar Dene'),
                ),
              ],
            ),
          ),
        ],
      ],
    );
  }
}

class _ImageThumbnail extends StatelessWidget {
  const _ImageThumbnail({required this.file, required this.onRemove});
  final XFile file;
  final VoidCallback onRemove;

  @override
  Widget build(BuildContext context) {
    return Stack(
      children: [
        ClipRRect(
          borderRadius: BorderRadius.circular(12),
          child: FutureBuilder<Uint8List>(
            future: file.readAsBytes(),
            builder: (context, snapshot) {
              if (!snapshot.hasData) {
                return AspectRatio(
                  aspectRatio: 16 / 9,
                  child: Container(
                    color: Theme.of(context).colorScheme.surfaceContainerHighest,
                    child: const Center(child: CircularProgressIndicator(strokeWidth: 2)),
                  ),
                );
              }
              return AspectRatio(
                aspectRatio: 16 / 9,
                child: Image.memory(
                  snapshot.data!,
                  width: double.infinity,
                  fit: BoxFit.cover,
                ),
              );
            },
          ),
        ),
        Positioned(
          top: 8,
          right: 8,
          child: IconButton.filled(
            onPressed: onRemove,
            icon: const Icon(Icons.close, size: 16),
            constraints: const BoxConstraints(minWidth: 32, minHeight: 32),
            padding: EdgeInsets.zero,
            style: IconButton.styleFrom(
              backgroundColor: Colors.black54,
              foregroundColor: Colors.white,
            ),
          ),
        ),
      ],
    );
  }
}


