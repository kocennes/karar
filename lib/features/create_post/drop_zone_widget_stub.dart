import 'package:flutter/material.dart';
import 'package:image_picker/image_picker.dart';

class DropZoneWidget extends StatelessWidget {
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
  Widget build(BuildContext context) => child;
}
