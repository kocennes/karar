import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:karar/shared/widgets/post_image.dart';

void main() {
  testWidgets('PostImage shows retry and hide actions on load error',
      (tester) async {
    await tester.pumpWidget(
      const MaterialApp(
        home: Scaffold(
          body: PostImage(
            imageUrl: 'https://example.invalid/missing.jpg',
            aspectRatio: 16 / 9,
            forceError: true,
          ),
        ),
      ),
    );

    await tester.pump();

    expect(find.text('Tekrar dene'), findsOneWidget);
    expect(find.text('Gizle'), findsOneWidget);

    await tester.tap(find.text('Gizle'));
    await tester.pump();

    expect(find.text('Tekrar dene'), findsNothing);
    expect(find.text('Gizle'), findsNothing);
  });

  testWidgets('PostImage uses compact error actions for thumbnails',
      (tester) async {
    await tester.pumpWidget(
      const MaterialApp(
        home: Scaffold(
          body: PostImage(
            imageUrl: 'https://example.invalid/thumb.jpg',
            width: 76,
            height: 76,
            forceError: true,
          ),
        ),
      ),
    );

    await tester.pump();

    expect(find.byIcon(Icons.refresh), findsOneWidget);
    expect(find.byIcon(Icons.visibility_off_outlined), findsOneWidget);
  });
}
