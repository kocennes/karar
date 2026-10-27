import 'package:app_settings/app_settings.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:image_picker/image_picker.dart';

import '../../core/theme/app_typography.dart';
import '../../core/theme/karar_tokens.dart';
import '../../shared/widgets/karar_circle_button.dart';

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
    final kc = context.kc;

    if (permissionDenied) {
      return _StatusRow(
        icon: Icons.no_photography_rounded,
        message: 'Fotoğraf izni verilmedi.',
        background: kc.warnSoft,
        foreground: kc.warnText,
        action: TextButton(
          onPressed: () => AppSettings.openAppSettings(
            type: AppSettingsType.settings,
          ),
          child: const Text('Ayarlara Git'),
        ),
      );
    }

    final scheme = Theme.of(context).colorScheme;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        if (images.isNotEmpty)
          Wrap(
            spacing: KararSpace.s12,
            runSpacing: KararSpace.s12,
            children: [
              for (var i = 0; i < images.length; i++)
                _ImageThumbnail(
                  key: ObjectKey(images[i]),
                  file: images[i],
                  onRemove: () => onRemove(i),
                ),
            ],
          )
        else
          _AddPhotoTile(onTap: onPick, hasError: imageError != null),
        if (imageError != null) ...[
          const SizedBox(height: KararSpace.s8),
          _StatusRow(
            icon: Icons.error_outline_rounded,
            message: imageError!,
            background: scheme.errorContainer,
            foreground: scheme.onErrorContainer,
          ),
        ],
        if (uploadFailed) ...[
          const SizedBox(height: KararSpace.s8),
          _StatusRow(
            icon: Icons.cloud_off_rounded,
            message: 'Bazı görseller yüklenemedi.',
            background: scheme.errorContainer,
            foreground: scheme.onErrorContainer,
            action: TextButton(
              onPressed: onRetry,
              style: TextButton.styleFrom(
                foregroundColor: scheme.onErrorContainer,
              ),
              child: const Text('Tekrar Dene'),
            ),
          ),
        ],
      ],
    );
  }
}

/// 88 px drop tile: radius 20, 1.5 px dashed outline, primary icon + label.
class _AddPhotoTile extends StatelessWidget {
  const _AddPhotoTile({required this.onTap, required this.hasError});

  final VoidCallback onTap;
  final bool hasError;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final borderColor =
        hasError ? Theme.of(context).colorScheme.error : kc.outline;
    final shape = RoundedRectangleBorder(
      borderRadius: KararRadius.all(KararRadius.tile),
    );

    return Semantics(
      button: true,
      excludeSemantics: true,
      label: 'Fotoğraf Ekle',
      onTap: onTap,
      child: CustomPaint(
        foregroundPainter: _DashedRRectPainter(
          color: borderColor,
          radius: KararRadius.tile,
          strokeWidth: 1.5,
        ),
        child: Material(
          type: MaterialType.transparency,
          shape: shape,
          clipBehavior: Clip.antiAlias,
          child: InkWell(
            onTap: onTap,
            customBorder: shape,
            child: SizedBox(
              height: 88,
              child: Center(
                child: Padding(
                  padding:
                      const EdgeInsets.symmetric(horizontal: KararSpace.s16),
                  child: Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Icon(
                        Icons.add_photo_alternate_rounded,
                        size: 24,
                        color: kc.primary,
                      ),
                      const SizedBox(width: KararSpace.s10),
                      Flexible(
                        child: Text(
                          'Fotoğraf Ekle',
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: AppTypography.bodyStrong.copyWith(
                            color: kc.primaryText,
                          ),
                        ),
                      ),
                    ],
                  ),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

/// Warn / error message row (radius 16) with an optional trailing action.
class _StatusRow extends StatelessWidget {
  const _StatusRow({
    required this.icon,
    required this.message,
    required this.background,
    required this.foreground,
    this.action,
  });

  final IconData icon;
  final String message;
  final Color background;
  final Color foreground;
  final Widget? action;

  @override
  Widget build(BuildContext context) {
    return Semantics(
      liveRegion: true,
      child: Container(
        width: double.infinity,
        constraints: const BoxConstraints(minHeight: 48),
        padding: EdgeInsets.fromLTRB(
          KararSpace.s16,
          KararSpace.s6,
          action == null ? KararSpace.s16 : KararSpace.s4,
          KararSpace.s6,
        ),
        decoration: BoxDecoration(
          color: background,
          borderRadius: KararRadius.all(KararRadius.input),
        ),
        child: Row(
          children: [
            Icon(icon, size: 18, color: foreground),
            const SizedBox(width: KararSpace.s10),
            Expanded(
              child: Text(
                message,
                style: AppTypography.labelS.copyWith(
                  color: foreground,
                  fontWeight: FontWeight.w600,
                ),
              ),
            ),
            if (action != null) action!,
          ],
        ),
      ),
    );
  }
}

class _ImageThumbnail extends StatefulWidget {
  const _ImageThumbnail({super.key, required this.file, required this.onRemove});

  final XFile file;
  final VoidCallback onRemove;

  @override
  State<_ImageThumbnail> createState() => _ImageThumbnailState();
}

class _ImageThumbnailState extends State<_ImageThumbnail> {
  late Future<Uint8List> _bytes = widget.file.readAsBytes();

  @override
  void didUpdateWidget(_ImageThumbnail oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.file != widget.file) _bytes = widget.file.readAsBytes();
  }

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    const size = 88.0;
    return SizedBox(
      width: size,
      height: size,
      child: Stack(
        clipBehavior: Clip.none,
        children: [
          ClipRRect(
            borderRadius: KararRadius.all(KararRadius.input),
            child: FutureBuilder<Uint8List>(
              future: _bytes,
              builder: (context, snapshot) {
                if (!snapshot.hasData) {
                  return ColoredBox(
                    color: kc.muted,
                    child: const Center(
                      child: SizedBox(
                        width: 20,
                        height: 20,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      ),
                    ),
                  );
                }
                return Image.memory(
                  snapshot.data!,
                  width: size,
                  height: size,
                  fit: BoxFit.cover,
                  cacheWidth: (size * 3).round(),
                );
              },
            ),
          ),
          // 28 px visual remove circle (black 55 %), 44 px hit area.
          Positioned(
            top: -2,
            right: -2,
            child: KararCircleButton(
              icon: Icons.close_rounded,
              onPressed: widget.onRemove,
              tooltip: 'Kaldır',
              style: KararCircleStyle.onColor,
              backgroundColor: kc.mediaBackdrop.withValues(alpha: 0.55),
              size: 28,
              iconSize: 16,
            ),
          ),
        ],
      ),
    );
  }
}

/// Dashed rounded-rectangle outline (drop zone tile).
class _DashedRRectPainter extends CustomPainter {
  const _DashedRRectPainter({
    required this.color,
    required this.radius,
    required this.strokeWidth,
  });

  final Color color;
  final double radius;
  final double strokeWidth;

  static const _dash = 6.0;
  static const _gap = 5.0;

  @override
  void paint(Canvas canvas, Size size) {
    final inset = strokeWidth / 2;
    final rrect = RRect.fromRectAndRadius(
      Rect.fromLTWH(inset, inset, size.width - strokeWidth,
          size.height - strokeWidth),
      Radius.circular(radius - inset),
    );
    final paint = Paint()
      ..color = color
      ..style = PaintingStyle.stroke
      ..strokeWidth = strokeWidth
      ..strokeCap = StrokeCap.round;
    final path = Path()..addRRect(rrect);
    for (final metric in path.computeMetrics()) {
      var distance = 0.0;
      while (distance < metric.length) {
        final end = (distance + _dash).clamp(0.0, metric.length);
        canvas.drawPath(metric.extractPath(distance, end), paint);
        distance += _dash + _gap;
      }
    }
  }

  @override
  bool shouldRepaint(_DashedRRectPainter oldDelegate) =>
      oldDelegate.color != color ||
      oldDelegate.radius != radius ||
      oldDelegate.strokeWidth != strokeWidth;
}
