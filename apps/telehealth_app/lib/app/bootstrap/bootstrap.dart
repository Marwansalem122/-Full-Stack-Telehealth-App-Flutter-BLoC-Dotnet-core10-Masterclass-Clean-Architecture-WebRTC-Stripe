
import 'package:flutter/material.dart';
import 'package:telehealth_app/app/di/injection.dart';
import 'package:telehealth_app/telehealth_app.dart';

Future<void> bootstrap() async {
  WidgetsFlutterBinding.ensureInitialized();

  await configureDependencies();

  runApp(const TelehealthApp());
}