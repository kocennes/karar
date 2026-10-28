import 'dart:async';
// ignore_for_file: avoid_web_libraries_in_flutter, deprecated_member_use

import 'dart:html' as html;

import 'package:flutter/material.dart';
import 'package:image_picker/image_picker.dart';

import '../../core/theme/app_typography.dart';
import '../../core/theme/karar_tokens.dart';
import '../../core/theme/motion.dart';

class DropZoneWidget extends StatefulWidget {
  const DropZoneWidget({
    super.key,
    required this.child,
    required this.onFileDrop,
    required this.onError,
  });

  final Widget child;
  final void Function(XFile file) onFileDrop;
  final void Function(String error) onError;

  @override
  State<DropZoneWidget> createState() => _DropZoneWidgetState();
}

class _DropZoneWidgetState extends State<DropZoneWidget> {
  bool _isDragging = false;
  Timer? _dragTimer;
  StreamSubscription<html.MouseEvent>? _dragOverSub;
  StreamSubscription<html.MouseEvent>? _dropSub;

  static const _allowedMimes = {'image/jpeg', 'image/png', 'image/webp'};
  static const _maxBytes = 5 * 1024 * 1024;

  @override
  void initState() {
    super.initState();
    _dragOverSub = html.document.onDragOver.listen(_onDragOver);
    _dropSub = html.document.onDrop.listen(_onDrop);
  }

  @override
  void dispose() {
    _dragTimer?.cancel();
    _dragOverSub?.cancel();
    _dropSub?.cancel();
    super.dispose();
  }

  void _onDragOver(html.MouseEvent e) {
    e.preventDefault();
    _dragTimer?.cancel();
    if (!_isDragging && mounted) setState(() => _isDragging = true);
    _dragTimer = Timer(const Duration(milliseconds: 300), () {
      if (_isDragging && mounted) setState(() => _isDragging = false);
    });
  }

  void _onDrop(html.MouseEvent e) {
    e.preventDefault();
    _dragTimer?.cancel();
    if (mounted) setState(() => _isDragging = false);

    final dataTransfer = (e as dynamic).dataTransfer as html.DataTransfer?;
    final files = dataTransfer?.files;
    if (files == null || files.isEmpty) return;

    final file = files[0];
    final mimeType = file.type;
    final size = file.size;

    if (!_allowedMimes.contains(mimeType)) {
      widget.onError('Desteklenmeyen tür. JPEG, PNG veya WebP kullanın.');
      return;
    }
    if (size > _maxBytes) {
      widget.onError('Görsel 5 MB\'dan küçük olmalı.');
      return;
    }

    final url = html.Url.createObjectUrlFromBlob(file);
    widget.onFileDrop(XFile(url, name: file.name, mimeType: mimeType));
  }

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final radius = KararRadius.all(KararRadius.tile);
    return AnimatedContainer(
      duration: KararMotion.d(context, KararMotion.base),
      curve: KararMotion.standard,
      decoration: _isDragging
          ? BoxDecoration(
              borderRadius: radius,
              border: Border.all(color: kc.primary, width: 2),
            )
          : null,
      child: Stack(
        children: [
          widget.child,
          if (_isDragging)
            Positioned.fill(
              child: Semantics(
                liveRegion: true,
                child: Container(
                  decoration: BoxDecoration(
                    color: kc.primaryContainer.withValues(alpha: 0.94),
                    borderRadius: radius,
                  ),
                  padding: const EdgeInsets.symmetric(
                    horizontal: KararSpace.s16,
                  ),
                  child: Center(
                    child: Row(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Icon(
                          Icons.upload_file_rounded,
                          size: 24,
                          color: kc.primary,
                        ),
                        const SizedBox(width: KararSpace.s10),
                        Flexible(
                          child: Text(
                            'Fotoğrafı buraya bırak',
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: AppTypography.bodyStrong.copyWith(
                              color: kc.onPrimaryContainer,
                            ),
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
              ),
            ),
        ],
      ),
    );
  }
}
